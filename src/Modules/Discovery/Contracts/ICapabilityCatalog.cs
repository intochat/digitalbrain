using DigitalBrain.Contracts;

namespace DigitalBrain.Discovery;

public enum CapabilityKind
{
    App = 0,
    Operation = 1,
    Agent = 2,
    SemanticType = 3,
    WorkspaceInstance = 4,
}

[GenerateSerializer, Alias("discovery.hit")]
public sealed record CapabilityHit
{
    [Id(0)] public required string Id { get; init; }
    [Id(1)] public required CapabilityKind Kind { get; init; }
    [Id(2)] public required double Score { get; init; }
}

[GenerateSerializer, Alias("discovery.result")]
public sealed record CapabilitySearchResult
{
    [Id(0)] public IReadOnlyList<CapabilityHit> Hits { get; init; } = [];
    [Id(1)] public bool Degraded { get; init; }
}

// Search returns ids only; the manifest catalog is the truth.
[Alias("capability-catalog")]
[Orleans.Metadata.DefaultGrainType("capability-catalog")]
public interface ICapabilityCatalog : INeuron
{
    Task<CapabilitySearchResult> Search(string query, string workspaceId, int take);
}
