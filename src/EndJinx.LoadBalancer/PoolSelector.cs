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

public class PoolSelector
{
    private readonly List<Backend> _backends;
    private int _next = 0;
    private readonly Lock _selectionLock = new();

    public PoolSelector(List<Backend> backendObjs)
    {
        if (backendObjs.Count != backendObjs.Distinct().Count())
        {
            throw new ArgumentException("Backend endpoints must be unique.");
        }

        _backends = backendObjs
            .Select(backend => new Backend(backend.Host, backend.Port, backend.IsHealthy))
            .ToList();
    }

    public Backend Next()
    {
        lock (_selectionLock)
        {
            if (_backends.Count == 0)
            {
                throw new InvalidOperationException("Error: The backend pool is empty.");
            }

            int attempts = 0;
            while (attempts < _backends.Count)
            {
                var backend = _backends[_next];
                if (backend.IsHealthy)
                {
                    _next = (_next + 1) % _backends.Count;
                    return backend;
                }

                _next = (_next + 1) % _backends.Count;
                attempts++;
            }

            throw new InvalidOperationException("Error: Can't connect to any backends.");
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
}