using System.Net;
using System.Net.Sockets;
using EndJinx.LoadBalancer;

var configurationPath = GetOption(args, "--config") ?? Path.Combine("config", "load-balancer.yaml");
var configuration = LoadBalancerConfigurationLoader.Load(configurationPath);
var selectors = configuration.Pools.ToDictionary(
    pool => pool.Key,
    pool => new PoolSelector(
        pool.Value.Select(backend => new Backend(backend.Host, backend.Port)).ToList(),
        CreateStrategy(configuration.Strategy)));

var listenerAddress = ResolveAddress(configuration.Listen.Host);
var listener = new TcpListener(listenerAddress, configuration.Listen.Port);
listener.Start();
_ = PeriodicHealthCheckAsync(selectors, configuration.HealthCheck);

Console.WriteLine(
    $"Layer {configuration.Mode[1..].ToUpperInvariant()} load balancer listening on " +
    $"{configuration.Listen.Host}:{configuration.Listen.Port} using {configuration.Strategy}");

while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();
    _ = Task.Run(() => HandleClientAsync(client, configuration, selectors));
}

static async Task HandleClientAsync(
    TcpClient client,
    LoadBalancerConfiguration configuration,
    IReadOnlyDictionary<string, PoolSelector> selectors)
{
    if (configuration.Mode == "l7")
    {
        var proxy = new HttpProxy(
            configuration.Routes,
            selectors,
            failedBackend => MarkBackendUnhealthy(selectors, failedBackend),
            completedBackend => ReleaseBackend(selectors, completedBackend));
        await proxy.HandleAsync(client);
        return;
    }

    var selector = selectors.Values.Single();
    try
    {
        var backend = selector.Next();
        Console.WriteLine($"Forwarding TCP connection to {backend.Host}:{backend.Port}");
        var proxy = new TcpProxy(
            backend,
            failedBackend => selector.MarkUnHealthy(failedBackend),
            completedBackend => selector.Release(completedBackend));
        await proxy.HandleAsync(client);
    }
    catch (InvalidOperationException exception)
    {
        Console.Error.WriteLine(exception.Message);
        client.Dispose();
    }
}

static async Task PeriodicHealthCheckAsync(
    IReadOnlyDictionary<string, PoolSelector> selectors,
    HealthCheckConfiguration healthCheckConfiguration)
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
                    Console.WriteLine($"Backend {backend.Host}:{backend.Port} in pool '{poolName}' recovered");
                    selector.MarkHealthy(backend);
                }
                else if (!isHealthy && backend.IsHealthy)
                {
                    selector.MarkUnHealthy(backend);
                    Console.WriteLine($"Backend {backend.Host}:{backend.Port} in pool '{poolName}' marked unhealthy");
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
        "random" => new RandomSelectionStrategy(),
        "least-connections" => new LeastConnectionsSelectionStrategy(),
        _ => throw new ArgumentException(
            "The strategy must be 'round-robin', 'random', or 'least-connections'.",
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