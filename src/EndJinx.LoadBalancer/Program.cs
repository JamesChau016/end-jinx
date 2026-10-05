using System.Net;
using System.Net.Sockets;
using EndJinx.LoadBalancer;

var configurationPath = GetOption(args, "--config") ?? Path.Combine("config", "load-balancer.yaml");
var configuration = LoadBalancerConfigurationLoader.Load(configurationPath);
var logger = new LoadBalancerLogger();
var metrics = new LoadBalancerMetrics();
var selectors = configuration.Pools.ToDictionary(
    pool => pool.Key,
    pool => new PoolSelector(
        pool.Value.Select(backend =>
            configuration.Strategy.Equals("weighted-round-robin", StringComparison.OrdinalIgnoreCase)
                ? new Backend(backend.Host, backend.Port) { Weight = backend.Weight }
                : new Backend(backend.Host, backend.Port)).ToList(),
        CreateStrategy(configuration.Strategy)));

foreach (var selector in selectors.Values)
{
    foreach (var backend in selector.Backends)
    {
        metrics.RegisterBackend(backend);
    }
}

var listenerAddress = ResolveAddress(configuration.Listen.Host);
var listener = new TcpListener(listenerAddress, configuration.Listen.Port);
listener.Start();
_ = PeriodicHealthCheckAsync(selectors, configuration.HealthCheck, metrics, logger);

logger.LogStartup(configuration);

while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();
    _ = Task.Run(() => HandleClientAsync(client, configuration, selectors, metrics, logger));
}

static async Task HandleClientAsync(
    TcpClient client,
    LoadBalancerConfiguration configuration,
    IReadOnlyDictionary<string, PoolSelector> selectors,
    LoadBalancerMetrics metrics,
    LoadBalancerLogger logger)
{
    if (configuration.Mode == "l7")
    {
        var proxy = new HttpProxy(
            configuration.Routes,
            selectors,
            failedBackend => MarkBackendUnhealthy(selectors, failedBackend),
            completedBackend => ReleaseBackend(selectors, completedBackend),
            requestObserver: (backend, duration, succeeded) =>
            {
                metrics.RequestCompleted(backend, duration, succeeded);
                logger.LogRequest("HTTP", backend, duration, succeeded);
            },
            timeoutMilliseconds: configuration.EffectiveConnectionTimeoutMilliseconds,
            maxRetries: configuration.EffectiveMaxRetries,
            logger: logger,
            connectionStartedObserver: metrics.ConnectionStarted);
        await proxy.HandleAsync(client);
        return;
    }

    var selector = selectors.Values.Single();
    try
    {
        for (var attempt = 0; attempt <= configuration.EffectiveMaxRetries; attempt++)
        {
            var backend = selector.Next();
            metrics.ConnectionStarted(backend);
            logger.LogRouting("TCP", backend);
            var proxy = new TcpProxy(
                backend,
                failedBackend => selector.MarkUnHealthy(failedBackend),
                completedBackend => selector.Release(completedBackend),
                (completedBackend, duration, succeeded) =>
                {
                    metrics.RequestCompleted(completedBackend, duration, succeeded);
                    logger.LogRequest("TCP", completedBackend, duration, succeeded);
                },
                configuration.EffectiveConnectionTimeoutMilliseconds,
                logger);

            if (await proxy.HandleAsync(client))
            {
                return;
            }

            if (attempt == configuration.EffectiveMaxRetries)
            {
                logger.LogFailure("No healthy backends remain for the TCP request.");
                client.Dispose();
                return;
            }
        }
    }
    catch (InvalidOperationException exception)
    {
        logger.LogFailure(exception.Message);
        client.Dispose();
    }
}

static async Task PeriodicHealthCheckAsync(
    IReadOnlyDictionary<string, PoolSelector> selectors,
    HealthCheckConfiguration healthCheckConfiguration,
    LoadBalancerMetrics metrics,
    LoadBalancerLogger logger)
{
    var healthCheck = new HealthCheck(healthCheckConfiguration.TimeoutMilliseconds);

    while (true)
    {
        foreach (var (poolName, selector) in selectors)
        {
            foreach (var backend in selector.Backends)
            {
                var isHealthy = await healthCheck.CheckAsync(backend);
                if (isHealthy && !backend.IsHealthy)
                {
                    logger.LogHealthChange(poolName, backend, isHealthy: true);
                    selector.MarkHealthy(backend);
                    metrics.SetHealth(backend, isHealthy: true);
                }
                else if (!isHealthy && backend.IsHealthy)
                {
                    selector.MarkUnHealthy(backend);
                    metrics.SetHealth(backend, isHealthy: false);
                    logger.LogHealthChange(poolName, backend, isHealthy: false);
                }
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(healthCheckConfiguration.IntervalSeconds));
    }
}

static void MarkBackendUnhealthy(IReadOnlyDictionary<string, PoolSelector> selectors, Backend backend)
{
    foreach (var selector in selectors.Values)
    {
        selector.MarkUnHealthy(backend);
    }
}

static void ReleaseBackend(IReadOnlyDictionary<string, PoolSelector> selectors, Backend backend)
{
    foreach (var selector in selectors.Values)
    {
        try
        {
            selector.Release(backend);
            return;
        }
        catch (ArgumentException)
        {
            // This backend belongs to another pool.
        }
        catch (InvalidOperationException)
        {
            // This backend has already been released.
        }
    }
}

static IPAddress ResolveAddress(string host)
{
    if (host is "*" or "0.0.0.0")
    {
        return IPAddress.Any;
    }

    if (host is "::" or "[::]")
    {
        return IPAddress.IPv6Any;
    }

    return IPAddress.Parse(host);
}

static IBackendSelectionStrategy CreateStrategy(string strategyName)
{
    return strategyName.ToLowerInvariant() switch
    {
        "round-robin" => new RoundRobinSelectionStrategy(),
        "weighted-round-robin" => new WeightedRoundRobinStrategy(),
        "random" => new RandomSelectionStrategy(),
        "least-connections" => new LeastConnectionsSelectionStrategy(),
        _ => throw new ArgumentException(
            "The strategy must be 'round-robin', 'weighted-round-robin', 'random', or 'least-connections'.",
            nameof(strategyName))
    };
}

static string? GetOption(string[] arguments, string optionName)
{
    var optionIndex = Array.IndexOf(arguments, optionName);
    if (optionIndex < 0)
    {
        return null;
    }

    if (optionIndex + 1 >= arguments.Length)
    {
        throw new ArgumentException($"The {optionName} option requires a value.");
    }

    return arguments[optionIndex + 1];
}