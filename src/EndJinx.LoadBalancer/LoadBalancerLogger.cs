namespace EndJinx.LoadBalancer;

public sealed class LoadBalancerLogger
{
    private readonly object _consoleLock = new();

    public void LogStartup(LoadBalancerConfiguration configuration)
    {
        Write(
            $"Layer {configuration.Mode[1..].ToUpperInvariant()} load balancer listening on " +
            $"{configuration.Listen.Host}:{configuration.Listen.Port} using {configuration.Strategy}" +
            $" (timeout {configuration.EffectiveConnectionTimeoutMilliseconds}ms, retries {configuration.EffectiveMaxRetries})");
    }

    public void LogRequest(
        string protocol,
        Backend backend,
        TimeSpan duration,
        bool succeeded,
        string? detail = null)
    {
        var outcome = succeeded ? "completed" : "failed";
        var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" ({detail})";
        Write($"{protocol} request {outcome}: backend={backend.Host}:{backend.Port}, duration={duration.TotalMilliseconds:F1}ms{suffix}");
    }

    public void LogRouting(string protocol, Backend backend, string? detail = null)
    {
        var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $", {detail}";
        Write($"{protocol} routing: backend={backend.Host}:{backend.Port}{suffix}");
    }

    public void LogHealthChange(string poolName, Backend backend, bool isHealthy)
    {
        var state = isHealthy ? "recovered" : "marked unhealthy";
        Write($"Backend {backend.Host}:{backend.Port} in pool '{poolName}' {state}");
    }

    public void LogFailure(string message)
    {
        Write(message, error: true);
    }

    private void Write(string message, bool error = false)
    {
        lock (_consoleLock)
        {
            if (error)
            {
                Console.Error.WriteLine(message);
            }
            else
            {
                Console.WriteLine(message);
            }
        }
    }
}
