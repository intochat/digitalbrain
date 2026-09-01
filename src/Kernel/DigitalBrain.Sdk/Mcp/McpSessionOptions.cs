namespace DigitalBrain.Sdk;

public sealed record McpSessionOptions
{
    public TimeSpan? Lifetime { get; init; }

    public int Capacity { get; init; } = 128;

    public long? ResponseBudgetBytes { get; init; }

    public TimeSpan? Timeout { get; init; }
}
