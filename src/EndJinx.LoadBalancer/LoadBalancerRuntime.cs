using System.Net;
using System.Net.Sockets;

namespace EndJinx.LoadBalancer;

public sealed class LoadBalancerRuntime
{
    private readonly LoadBalancerConfiguration _configuration;
    private readonly LoadBalancerLogger _logger;
    private readonly LoadBalancerMetrics _metrics;
    private readonly IReadOnlyDictionary<string, PoolSelector> _selectors;

    public LoadBalancerRuntime(
        LoadBalancerConfiguration configuration,
        LoadBalancerLogger? logger = null,
        LoadBalancerMetrics? metrics = null)
    {
        _configuration = configuration;
        _logger = logger ?? new LoadBalancerLogger();
        _metrics = metrics ?? new LoadBalancerMetrics();
        _selectors = CreateSelectors(configuration);

        foreach (var selector in _selectors.Values)
        {
            foreach (var backend in selector.Backends)
            {
                _metrics.RegisterBackend(backend);
            }
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var listener = new TcpListener(
            ResolveAddress(_configuration.Listen.Host),
            _configuration.Listen.Port);
        listener.Start();
        _logger.LogStartup(_configuration);

        var healthCheckTask = RunHealthChecksAsync(cancellationToken);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = HandleClientSafelyAsync(client);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            listener.Stop();
            await healthCheckTask;
        }
    }

    private async Task HandleClientSafelyAsync(TcpClient client)
    {
        try
        {
            await HandleClientAsync(client);
        }
        catch (Exception exception)
        {
            _logger.LogFailure($"Unhandled client error: {exception.Message}");
            client.Dispose();
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        if (_configuration.Mode == "l7")
        {
            var proxy = new HttpProxy(
                _configuration.Routes,
                _selectors,
                failedBackend => MarkBackendUnhealthy(failedBackend),
                completedBackend => ReleaseBackend(completedBackend),
                requestObserver: (backend, duration, succeeded) =>
                {
                    _metrics.RequestCompleted(backend, duration, succeeded);
                    _logger.LogRequest("HTTP", backend, duration, succeeded);
                },
                timeoutMilliseconds: _configuration.EffectiveConnectionTimeoutMilliseconds,
                maxRetries: _configuration.EffectiveMaxRetries,
                logger: _logger,
                connectionStartedObserver: _metrics.ConnectionStarted);
            await proxy.HandleAsync(client);
            return;
        }

        var selector = _selectors.Values.Single();
        try
        {
            for (var attempt = 0; attempt <= _configuration.EffectiveMaxRetries; attempt++)
            {
                var backend = selector.Next();
                _metrics.ConnectionStarted(backend);
                _logger.LogRouting("TCP", backend);
                var proxy = new TcpProxy(
                    backend,
                    failedBackend => selector.MarkUnHealthy(failedBackend),
                    completedBackend => selector.Release(completedBackend),
                    (completedBackend, duration, succeeded) =>
                    {
                        _metrics.RequestCompleted(completedBackend, duration, succeeded);
                        _logger.LogRequest("TCP", completedBackend, duration, succeeded);
                    },
                    _configuration.EffectiveConnectionTimeoutMilliseconds,
                    _logger);

                if (await proxy.HandleAsync(client))
                {
                    return;
                }

                if (attempt == _configuration.EffectiveMaxRetries)
                {
                    _logger.LogFailure("No healthy backends remain for the TCP request.");
                    client.Dispose();
                    return;
                }
            }
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogFailure(exception.Message);
            client.Dispose();
        }
    }

    private async Task RunHealthChecksAsync(CancellationToken cancellationToken)
    {
        var healthCheck = new HealthCheck(_configuration.HealthCheck.TimeoutMilliseconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var (poolName, selector) in _selectors)
            {
                foreach (var backend in selector.Backends)
                {
                    var isHealthy = await healthCheck.CheckAsync(backend);
                    if (isHealthy && !backend.IsHealthy)
                    {
                        selector.MarkHealthy(backend);
                        _metrics.SetHealth(backend, isHealthy: true);
                        _logger.LogHealthChange(poolName, backend, isHealthy: true);
                    }
                    else if (!isHealthy && backend.IsHealthy)
                    {
                        selector.MarkUnHealthy(backend);
                        _metrics.SetHealth(backend, isHealthy: false);
                        _logger.LogHealthChange(poolName, backend, isHealthy: false);
                    }
                }
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_configuration.HealthCheck.IntervalSeconds),
                cancellationToken);
        }
    }

    private void MarkBackendUnhealthy(Backend backend)
    {
        foreach (var selector in _selectors.Values)
        {
            try
            {
                selector.MarkUnHealthy(backend);
                _metrics.SetHealth(backend, isHealthy: false);
            }
            catch (ArgumentException)
            {
            }
        }
    }

    private void ReleaseBackend(Backend backend)
    {
        foreach (var selector in _selectors.Values)
        {
            try
            {
                selector.Release(backend);
                return;
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private static IReadOnlyDictionary<string, PoolSelector> CreateSelectors(
        LoadBalancerConfiguration configuration)
    {
        return configuration.Pools.ToDictionary(
            pool => pool.Key,
            pool => new PoolSelector(
                pool.Value.Select(backend => new Backend(
                    backend.Host,
                    backend.Port,
                    weight: backend.Weight)).ToList(),
                SelectionStrategyConfig.Create(configuration.Strategy)));
    }

    private static IPAddress ResolveAddress(string host)
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
}
