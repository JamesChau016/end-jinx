using EndJinx.LoadBalancer;

namespace EndJinx.Tests;

public class LoadBalancerConfigurationTests
{
    [Fact]
    public void Load_ReadsYamlConfiguration()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """
            mode: l7
            listen:
              host: 127.0.0.1
              port: 9000
            strategy: least-connections
            timeoutMilliseconds: 2500
            maxRetries: 3
            healthCheck:
              intervalSeconds: 2
              timeoutMilliseconds: 250
            pools:
              api:
                - host: 127.0.0.1
                  port: 8000
            routes:
              - pathPrefix: /api
                pool: api
            """);

        try
        {
            var configuration = LoadBalancerConfigurationLoader.Load(path);

            Assert.Equal("l7", configuration.Mode);
            Assert.Equal("least-connections", configuration.Strategy);
            Assert.Equal(2500, configuration.TimeoutMilliseconds);
            Assert.Equal(3, configuration.MaxRetries);
            Assert.Equal(250, configuration.HealthCheck.TimeoutMilliseconds);
            Assert.Equal(8000, configuration.Pools["api"][0].Port);
            Assert.Equal("/api", configuration.Routes[0].PathPrefix);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_RejectsNonPositiveWeightedRoundRobinWeights()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """
            strategy: weighted-round-robin
            pools:
              default:
              - host: 127.0.0.1
                port: 8000
                weight: 0
            """);

        try
        {
            var exception = Assert.Throws<ArgumentException>(() =>
              LoadBalancerConfigurationLoader.Load(path));

            Assert.Contains("weights must be greater than 0", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}