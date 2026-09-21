using System.Net;
using System.Net.Sockets;
using EndJinx.LoadBalancer;

var strategyName = GetOption(args, "--strategy") ?? "round-robin";
var positionalArgs = args.Where((argument, index) =>
    argument != "--strategy" && (index == 0 || args[index - 1] != "--strategy"));
int listenPort = !positionalArgs.Any() ? 9000 : ParsePort(positionalArgs.First(), "load balancer");
IEnumerable<int> backendPorts = positionalArgs.Count() <= 1
    ? [8000, 8001]
    : positionalArgs.Skip(1).Select(port => ParsePort(port, "backend"));
List<Backend> pool = backendPorts
    .Select(port => new Backend("127.0.0.1", port))
    .ToList();

var listener = new TcpListener(IPAddress.Any, listenPort);
var selector = new PoolSelector(pool, CreateStrategy(strategyName));
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
        failedBackend => selector.MarkUnHealthy(failedBackend),
        completedBackend => selector.Release(completedBackend)
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


