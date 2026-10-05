using System.Collections.Concurrent;

namespace EndJinx.LoadBalancer;

public sealed record BackendMetricsSnapshot(
    string Backend,
    long Requests,
    long Failures,
    int ActiveConnections,
    TimeSpan TotalDuration,
    bool IsHealthy);

public sealed class LoadBalancerMetrics
{
    private readonly ConcurrentDictionary<string, BackendMetrics> _backends = new();

    public void RegisterBackend(Backend backend)
    {
        _backends.TryAdd(GetKey(backend), new BackendMetrics(backend.IsHealthy));
    }

    public void SetHealth(Backend backend, bool isHealthy)
    {
        var metrics = GetMetrics(backend);
        metrics.IsHealthy = isHealthy;
    }

    public void ConnectionStarted(Backend backend)
    {
        var metrics = GetMetrics(backend);
        Interlocked.Increment(ref metrics.ActiveConnections);
    }

    public void RequestCompleted(Backend backend, TimeSpan duration, bool succeeded)
    {
        var metrics = GetMetrics(backend);
        Interlocked.Decrement(ref metrics.ActiveConnections);
        Interlocked.Increment(ref metrics.Requests);
        if (!succeeded)
        {
            Interlocked.Increment(ref metrics.Failures);
        }

        Interlocked.Add(ref metrics.TotalDurationTicks, duration.Ticks);
    }

    public IReadOnlyList<BackendMetricsSnapshot> Snapshot()
    {
        return _backends
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry =>
            {
                var metrics = entry.Value;
                return new BackendMetricsSnapshot(
                    entry.Key,
                    Interlocked.Read(ref metrics.Requests),
                    Interlocked.Read(ref metrics.Failures),
                    Math.Max(0, Volatile.Read(ref metrics.ActiveConnections)),
                    TimeSpan.FromTicks(Interlocked.Read(ref metrics.TotalDurationTicks)),
                    metrics.IsHealthy);
            })
            .ToArray();
    }

    private BackendMetrics GetMetrics(Backend backend)
    {
        return _backends.GetOrAdd(GetKey(backend), _ => new BackendMetrics(backend.IsHealthy));
    }

    private static string GetKey(Backend backend) => $"{backend.Host}:{backend.Port}";

    private sealed class BackendMetrics(bool isHealthy)
    {
        public long Requests;
        public long Failures;
        public long TotalDurationTicks;
        public int ActiveConnections;
        public bool IsHealthy = isHealthy;
    }
}
