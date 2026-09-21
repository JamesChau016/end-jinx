using System.Collections.Concurrent;
using System.Net.Sockets;
using EndJinx.LoadBalancer;

namespace EndJinx.Tests;

public class PoolSelectorTests
{
    [Fact]
    public void Next_ReturnsBackendsInRoundRobinOrder()
    {
        var selector = new PoolSelector([
            new Backend("127.0.0.1", 8000),
            new Backend("127.0.0.1", 8001),
            new Backend("127.0.0.1", 8002)
        ]);

        var selectedPorts = Enumerable.Range(0, 6)
            .Select(_ => selector.Next().Port)
            .ToArray();

        Assert.Equal([8000, 8001, 8002, 8000, 8001, 8002], selectedPorts);
    }

    [Fact]
    public void Next_UsesRandomSelectionStrategy()
    {
        var selector = new PoolSelector([
            new Backend("127.0.0.1", 8000),
            new Backend("127.0.0.1", 8001),
            new Backend("127.0.0.1", 8002)
        ], new RandomSelectionStrategy(new FixedRandom(2, 0)));

        var selectedPorts = Enumerable.Range(0, 2)
            .Select(_ => selector.Next().Port)
            .ToArray();

        Assert.Equal([8002, 8000], selectedPorts);
    }

    [Fact]
    public void Next_UsesLeastConnectionsStrategy()
    {
        var selector = new PoolSelector([
            new Backend("127.0.0.1", 8000),
            new Backend("127.0.0.1", 8001)
        ], new LeastConnectionsSelectionStrategy());

        var first = selector.Next();
        var second = selector.Next();
        var third = selector.Next();

        Assert.Equal(8000, first.Port);
        Assert.Equal(8001, second.Port);
        Assert.Equal(8000, third.Port);
        Assert.Equal(2, selector.ActiveConnections(first));
        Assert.Equal(1, selector.ActiveConnections(second));

        selector.Release(first);
        Assert.Equal(1, selector.ActiveConnections(first));
    }

    [Fact]
    public void Release_ThrowsWhenThereIsNoActiveConnection()
    {
        var selector = new PoolSelector([new Backend("127.0.0.1", 8000)]);

        Assert.Throws<InvalidOperationException>(() =>
            selector.Release(new Backend("127.0.0.1", 8000)));
    }

    [Fact]
    public void Next_ReusesTheOnlyBackend()
    {
        var selector = new PoolSelector([new Backend("127.0.0.1", 8000)]);

        var selectedPorts = Enumerable.Range(0, 3)
            .Select(_ => selector.Next().Port)
            .ToArray();

        Assert.Equal([8000, 8000, 8000], selectedPorts);
    }

    [Fact]
    public void Next_ThrowsForAnEmptyPool()
    {
        var selector = new PoolSelector([]);

        Assert.Throws<InvalidOperationException>(() => selector.Next());
    }

    [Fact]
    public void ConstructorCopiesTheBackendList()
    {
        var backends = new List<Backend>
        {
            new("127.0.0.1", 8000),
            new("127.0.0.1", 8001)
        };
        var selector = new PoolSelector(backends);
        backends[0] = new Backend("127.0.0.1", 9000);

        Assert.Equal(8000, selector.Next().Port);
    }

    [Fact]
    public void Next_SkipsUnhealthyBackendsUntilTheyRecover()
    {
        var selector = new PoolSelector([
            new Backend("127.0.0.1", 8000),
            new Backend("127.0.0.1", 8001),
            new Backend("127.0.0.1", 8002)
        ]);

        selector.MarkUnHealthy(new Backend("127.0.0.1", 8001));

        Assert.Equal([8000, 8002, 8000], Enumerable.Range(0, 3)
            .Select(_ => selector.Next().Port)
            .ToArray());

        selector.MarkHealthy(new Backend("127.0.0.1", 8001));

        Assert.Equal(8001, selector.Next().Port);
    }

    [Fact]
    public async Task TcpProxy_InvokesPassiveFailureCallback_WhenBackendConnectionFails()
    {
        var backend = new Backend("127.0.0.1", 65535, true);
        Backend? failedBackend = null;
        var proxy = new TcpProxy(backend, backendFailure => failedBackend = backendFailure);

        using var client = new TcpClient();
        await proxy.HandleAsync(client);

        Assert.NotNull(failedBackend);
        Assert.Equal(65535, failedBackend!.Port);
    }

    [Fact]
    public void Next_IsThreadSafe()
    {
        var selector = new PoolSelector([
            new Backend("127.0.0.1", 8000),
            new Backend("127.0.0.1", 8001),
            new Backend("127.0.0.1", 8002)
        ]);
        var selectedPorts = new ConcurrentBag<int>();

        Parallel.For(0, 300, _ => selectedPorts.Add(selector.Next().Port));

        Assert.Equal(100, selectedPorts.Count(port => port == 8000));
        Assert.Equal(100, selectedPorts.Count(port => port == 8001));
        Assert.Equal(100, selectedPorts.Count(port => port == 8002));
    }

    private sealed class FixedRandom(params int[] values) : Random
    {
        private int _index;

        public override int Next(int maxValue)
        {
            return values[_index++] % maxValue;
        }
    }
}
