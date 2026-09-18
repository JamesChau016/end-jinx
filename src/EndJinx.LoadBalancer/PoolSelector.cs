namespace EndJinx.LoadBalancer;

public record Backend(string Host, int Port);

public class PoolSelector
{
    private readonly List<Backend> _backends;
    private int _next = 0;
    private readonly Lock _selectionLock = new();

    public PoolSelector(List<Backend> backendObjs)
    {
        _backends = new List<Backend>(backendObjs);
    }

    public Backend Next()
    {
        lock (_selectionLock)
        {
            if (_backends.Count == 0)
            {
                throw new InvalidOperationException("Error: The backend pool is empty.");
            }
            Backend backendObj = _backends[_next];
            _next = (_next + 1) % _backends.Count;
            return backendObj;
        }
    }
}