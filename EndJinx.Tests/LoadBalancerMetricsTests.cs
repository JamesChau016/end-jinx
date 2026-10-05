using EndJinx.LoadBalancer;

namespace EndJinx.Tests;

public class LoadBalancerMetricsTests
{
    [Fact]
    public void Snapshot_ReportsRegisteredBackendWithZeroCounters()
    {
        var backend = new Backend("127.0.0.1", 8000);
        var metrics = new LoadBalancerMetrics();

        metrics.RegisterBackend(backend);

        var snapshot = Assert.Single(metrics.Snapshot());
        Assert.Equal("127.0.0.1:8000", snapshot.Backend);
        Assert.Equal(0, snapshot.Requests);
        Assert.Equal(0, snapshot.Failures);
        Assert.Equal(0, snapshot.ActiveConnections);
        Assert.Equal(TimeSpan.Zero, snapshot.TotalDuration);
        Assert.True(snapshot.IsHealthy);
    }

    [Fact]
    public void RequestCompleted_TracksSuccessAndDuration()
    {
        var backend = new Backend("127.0.0.1", 8000);
        var metrics = new LoadBalancerMetrics();
        var duration = TimeSpan.FromMilliseconds(125);

        metrics.RegisterBackend(backend);
        metrics.ConnectionStarted(backend);
        metrics.RequestCompleted(backend, duration, succeeded: true);

        var snapshot = Assert.Single(metrics.Snapshot());
        Assert.Equal(1, snapshot.Requests);
        Assert.Equal(0, snapshot.Failures);
        Assert.Equal(0, snapshot.ActiveConnections);
        Assert.Equal(duration, snapshot.TotalDuration);
    }

    [Fact]
    public void RequestCompleted_TracksFailures()
    {
        var backend = new Backend("127.0.0.1", 8000);
        var metrics = new LoadBalancerMetrics();

        metrics.ConnectionStarted(backend);
        metrics.RequestCompleted(backend, TimeSpan.FromMilliseconds(50), succeeded: false);

        var snapshot = Assert.Single(metrics.Snapshot());
        Assert.Equal(1, snapshot.Requests);
        Assert.Equal(1, snapshot.Failures);
        Assert.Equal(0, snapshot.ActiveConnections);
    }

    [Fact]
    public void SetHealth_UpdatesBackendHealthInSnapshot()
    {
        var backend = new Backend("127.0.0.1", 8000);
        var metrics = new LoadBalancerMetrics();

        metrics.RegisterBackend(backend);
        metrics.SetHealth(backend, isHealthy: false);

        var snapshot = Assert.Single(metrics.Snapshot());
        Assert.False(snapshot.IsHealthy);
    }

    [Fact]
    public void Snapshot_ReturnsBackendsInStableEndpointOrder()
    {
        var metrics = new LoadBalancerMetrics();

        metrics.RegisterBackend(new Backend("127.0.0.1", 8001));
        metrics.RegisterBackend(new Backend("127.0.0.1", 8000));

        Assert.Equal(
            ["127.0.0.1:8000", "127.0.0.1:8001"],
            metrics.Snapshot().Select(snapshot => snapshot.Backend));
    }
}
