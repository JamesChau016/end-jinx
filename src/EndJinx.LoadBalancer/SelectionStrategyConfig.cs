namespace EndJinx.LoadBalancer;

public static class SelectionStrategyConfig
{
    public static IBackendSelectionStrategy Create(string strategyName)
    {
        return strategyName.ToLowerInvariant() switch
        {
            "round-robin" => new RoundRobinSelectionStrategy(),
            "weighted-round-robin" => new WeightedRoundRobinStrategy(),
            "random" => new RandomSelectionStrategy(),
            "least-connections" => new LeastConnectionsSelectionStrategy(),
            _ => throw new ArgumentException(
                "The strategy must be 'round-robin', 'weighted-round-robin', 'random', or 'least-connections'.",
                nameof(strategyName))
        };
    }
}
