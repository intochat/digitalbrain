using DigitalBrain.Contracts;

namespace DigitalBrain.Discovery;

[GenerateSerializer, Alias("discovery.unmet-intent")]
public sealed record UnmetIntent
{
    [Id(0)] public required string Id { get; init; }
    [Id(1)] public required string WorkspaceId { get; init; }
    [Id(2)] public required string Text { get; init; }
    [Id(3)] public int Count { get; init; }
    [Id(4)] public DateTimeOffset LastSeenAt { get; init; }
}

// Discovery records what it could not satisfy so the board can show the gap.
[Alias("unmet-intents")]
[Orleans.Metadata.DefaultGrainType("unmet-intents")]
public interface IUnmetIntentBoard : INeuron
{
    Task Record(string workspaceId, string text, DateTimeOffset at);

    Task<IReadOnlyList<UnmetIntent>> Read();
}
