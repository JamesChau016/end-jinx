using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace EndJinx.LoadBalancer;

public sealed class LoadBalancerConfiguration
{
    public string Mode { get; set; } = "l4";
    public ListenerConfiguration Listen { get; set; } = new();
    public string Strategy { get; set; } = "round-robin";
    public HealthCheckConfiguration HealthCheck { get; set; } = new();
    public Dictionary<string, List<BackendConfiguration>> Pools { get; set; } = new();
    public List<RouteConfiguration> Routes { get; set; } = new();
}

public sealed class ListenerConfiguration
{
    public string Host { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 9000;
}

public sealed class HealthCheckConfiguration
{
    public int IntervalSeconds { get; set; } = 5;
    public int TimeoutMilliseconds { get; set; } = 1000;
}

public sealed class BackendConfiguration
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; }
}

public sealed class RouteConfiguration
{
    public string PathPrefix { get; set; } = "/";
    public string Pool { get; set; } = "default";
}

public static class LoadBalancerConfigurationLoader
{
    public static LoadBalancerConfiguration Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Load balancer configuration was not found: {path}", path);
        }

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var configuration = deserializer.Deserialize<LoadBalancerConfiguration>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("Load balancer configuration is empty.");

        Validate(configuration, path);
        return configuration;
    }

    private static void Validate(LoadBalancerConfiguration configuration, string path)
    {
        if (configuration.Mode is not ("l4" or "l7"))
        {
            throw new ArgumentException($"Configuration '{path}' mode must be 'l4' or 'l7'.");
        }

        if (configuration.Listen.Port is < 1 or > 65535)
        {
            throw new ArgumentException($"Configuration '{path}' listen.port must be between 1 and 65535.");
        }

        if (configuration.HealthCheck.IntervalSeconds < 1 || configuration.HealthCheck.TimeoutMilliseconds < 1)
        {
            throw new ArgumentException("Health-check interval and timeout must be positive.");
        }

        if (configuration.Pools.Count == 0)
        {
            throw new ArgumentException("At least one backend pool must be configured.");
        }

        if (configuration.Mode == "l4" && configuration.Pools.Count != 1)
        {
            throw new ArgumentException("L4 mode requires exactly one backend pool.");
        }

        foreach (var (name, backends) in configuration.Pools)
        {
            if (backends.Count == 0)
            {
                throw new ArgumentException($"Backend pool '{name}' must contain at least one backend.");
            }

            if (backends.Any(backend => backend.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(backend.Host)))
            {
                throw new ArgumentException($"Backend pool '{name}' contains an invalid backend.");
            }
        }

        if (configuration.Mode == "l7" && configuration.Routes.Count == 0)
        {
            throw new ArgumentException("L7 mode requires at least one route.");
        }

        foreach (var route in configuration.Routes)
        {
            if (string.IsNullOrWhiteSpace(route.PathPrefix) || !route.PathPrefix.StartsWith('/'))
            {
                throw new ArgumentException("Every route pathPrefix must start with '/'.");
            }

            if (!configuration.Pools.ContainsKey(route.Pool))
            {
                throw new ArgumentException($"Route '{route.PathPrefix}' references unknown pool '{route.Pool}'.");
            }
        }
    }
}