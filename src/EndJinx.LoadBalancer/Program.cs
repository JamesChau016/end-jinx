using System.Net;
using System.Net.Sockets;
using EndJinx.LoadBalancer;

int listenPort = args.Length == 0 ? 9000 : ParsePort(args[0], "load balancer");
IEnumerable<int> backendPorts = args.Length <= 1
    ? [8000, 8001]
    : args.Skip(1).Select(port => ParsePort(port, "backend"));
List<Backend> pool = backendPorts
    .Select(port => new Backend("127.0.0.1", port))
    .ToList();

var listener = new TcpListener(IPAddress.Any, listenPort);
var selector = new PoolSelector(pool);
int interval = 5;

listener.Start();
_ = PeriodicHealthCheckAsync(selector, pool, interval);
Console.WriteLine($"Load balancer listening on port {listenPort}");

while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();
    var backendObj = selector.Next();
    Console.WriteLine($"Forwarding TCP connections to {backendObj.Host}:{backendObj.Port}");
    var proxy = new TcpProxy(
        backendObj,
        failedBackend => selector.MarkUnHealthy(failedBackend)
    );
    _ = Task.Run(() => proxy.HandleAsync(client));
    
}

static async Task PeriodicHealthCheckAsync(PoolSelector selector, List<Backend> pool, int interval){

    var healthCheck = new HealthCheck();

    while (true)
    {
        foreach (var backend in pool)
        {
            var isHealthy = await healthCheck.CheckAsync(backend);

            if (isHealthy){
                if (!backend.IsHealthy){
                    Console.WriteLine($"Backend port {backend.Port} recovered");
                }
                selector.MarkHealthy(backend);
            }
            else{
                selector.MarkUnHealthy(backend);
                Console.WriteLine($"Backend port {backend.Port} marked unhealthy");
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(interval));
    }
}

static int ParsePort(string value, string name)
{
    if (!int.TryParse(value, out int port) || port is < 1 or > 65535)
    {
        throw new ArgumentException($"The {name} port must be between 1 and 65535.", nameof(value));
    }

    return port;
}


