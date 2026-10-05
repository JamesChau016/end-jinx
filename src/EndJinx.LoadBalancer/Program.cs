using EndJinx.LoadBalancer;

var configurationPath = GetOption(args, "--config") ?? Path.Combine("config", "load-balancer.yaml");
var configuration = LoadBalancerConfigurationLoader.Load(configurationPath);
await new LoadBalancerRuntime(configuration).RunAsync();

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
