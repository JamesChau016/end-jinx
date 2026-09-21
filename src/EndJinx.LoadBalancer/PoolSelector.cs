namespace EndJinx.LoadBalancer;

public class Backend
{
    public string Host { get; }
    public int Port { get; }
    public bool IsHealthy { get; set; } = true;

    public Backend(string host, int port, bool isHealthy = true)
    {
        Host = host;
        Port = port;
        IsHealthy = isHealthy;
    }
}

public interface IBackendSelectionStrategy
{
    Backend Select(
        IReadOnlyList<Backend> healthyBackends,
        IReadOnlyDictionary<Backend, int> activeConnections);
}

public sealed class RoundRobinSelectionStrategy : IBackendSelectionStrategy
{
    private int _next;

    public Backend Select(
        IReadOnlyList<Backend> healthyBackends,
        IReadOnlyDictionary<Backend, int> activeConnections)
    {
        var backend = healthyBackends[_next];
        _next = (_next + 1) % healthyBackends.Count;
        return backend;
    }
}

public sealed class RandomSelectionStrategy : IBackendSelectionStrategy
{
    private readonly Random _random;

    public RandomSelectionStrategy(Random? random = null)
    {
        _random = random ?? Random.Shared;
    }

    public Backend Select(
        IReadOnlyList<Backend> healthyBackends,
        IReadOnlyDictionary<Backend, int> activeConnections)
    {
        return healthyBackends[_random.Next(healthyBackends.Count)];
    }
}

public sealed class LeastConnectionsSelectionStrategy : IBackendSelectionStrategy
{
    public Backend Select(
        IReadOnlyList<Backend> healthyBackends,
        IReadOnlyDictionary<Backend, int> activeConnections)
    {
        return healthyBackends
            .OrderBy(backend => activeConnections[backend])
            .First();
    }
}

public class PoolSelector
{
    private readonly List<Backend> _backends;
    private readonly Dictionary<Backend, int> _activeConnections;
    private readonly IBackendSelectionStrategy _strategy;
    private readonly Lock _selectionLock = new();

    public PoolSelector(
        List<Backend> backendObjs,
        IBackendSelectionStrategy? strategy = null)
    {
        if (backendObjs.Count != backendObjs.Distinct().Count())
        {
            throw new ArgumentException("Backend endpoints must be unique.");
        }

        _backends = backendObjs
            .Select(backend => new Backend(backend.Host, backend.Port, backend.IsHealthy))
            .ToList();
        _activeConnections = _backends.ToDictionary(backend => backend, _ => 0);
        _strategy = strategy ?? new RoundRobinSelectionStrategy();
    }

    public Backend Next()
    {
        lock (_selectionLock)
        {
            if (_backends.Count == 0)
            {
                throw new InvalidOperationException("Error: The backend pool is empty.");
            }

            var healthyBackends = _backends
                .Where(backend => backend.IsHealthy)
                .ToList();

            if (healthyBackends.Count == 0)
            {
                throw new InvalidOperationException("Error: Can't connect to any backends.");
            }

            var backend = _strategy.Select(healthyBackends, _activeConnections);
            _activeConnections[backend]++;
            return backend;
        }
    }

    public void Release(Backend backend)
    {
        lock (_selectionLock)
        {
            var selectedBackend = FindBackend(backend);
            if (!_activeConnections.TryGetValue(selectedBackend, out int activeConnections))
            {
                throw new ArgumentException("Backend is not part of this pool.", nameof(backend));
            }

            if (activeConnections == 0)
            {
                throw new InvalidOperationException("Backend has no active connections to release.");
            }

            _activeConnections[selectedBackend] = activeConnections - 1;
        }
    }

    public int ActiveConnections(Backend backend)
    {
        lock (_selectionLock)
        {
            var selectedBackend = FindBackend(backend);
            return _activeConnections[selectedBackend];
        }
    }

    public Backend MarkHealthy(Backend backend)
    {
        return UpdateHealthState(backend, isHealthy: true);
    }

    public Backend MarkUnHealthy(Backend backend)
    {
        return UpdateHealthState(backend, isHealthy: false);
    }

    public IReadOnlyList<Backend> Backends => _backends;

    private Backend UpdateHealthState(Backend backend, bool isHealthy)
    {
        lock (_selectionLock)
        {
            int index = _backends.FindIndex(existing =>
                existing.Host == backend.Host &&
                existing.Port == backend.Port);

            if (index < 0)
            {
                throw new ArgumentException("Backend is not part of this pool.", nameof(backend));
            }

            _backends[index].IsHealthy = isHealthy;
            return _backends[index];
        }
    }

    private Backend FindBackend(Backend backend)
    {
        return _backends.FirstOrDefault(existing =>
            existing.Host == backend.Host &&
            existing.Port == backend.Port)
            ?? throw new ArgumentException("Backend is not part of this pool.", nameof(backend));
    }
}