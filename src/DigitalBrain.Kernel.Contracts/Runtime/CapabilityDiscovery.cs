using Orleans;

namespace DigitalBrain.Kernel.Capabilities;

public enum CapabilityOrigin
{
    Platform,
    Integration,
    Feature
}

public enum CapabilityResolutionKind
{
    Match,
    Ambiguous,
    Missing
}

[GenerateSerializer, Alias("digitalbrain.capability.descriptor.v1")]
public sealed record CapabilityDescriptor(
    [property: Id(0)] string Id,
    [property: Id(1)] int Version,
    [property: Id(2)] string Name,
    [property: Id(3)] string Description,
    [property: Id(4)] string[] Examples,
    [property: Id(5)] string[] RequiredGrants,
    [property: Id(6)] string[] RequiredConnections,
    [property: Id(7)] CapabilityOrigin Origin,
    [property: Id(8)] CapabilityOperationKind Kind,
    [property: Id(9)] bool Available);

[GenerateSerializer, Alias("digitalbrain.capability.resolution-receipt.v1")]
public sealed record CapabilityResolutionReceipt(
    [property: Id(0)] CapabilityResolutionKind Kind,
    [property: Id(1)] string? CapabilityId,
    [property: Id(2)] string? CapabilityName,
    [property: Id(3)] string[] CandidateIds,
    [property: Id(4)] double Confidence);

[GenerateSerializer, Alias("digitalbrain.feature.draft-reference.v1")]
public sealed record FeatureDraftReference(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] string Label,
    [property: Id(2)] string Route);

public sealed record CapabilitySearchRequest(
    string Prompt,
    IReadOnlySet<string> Grants,
    IReadOnlySet<string> Connections,
    int MaximumMatches = 3);

public sealed record CapabilityResolution(
    CapabilityResolutionReceipt Receipt,
    CapabilityDescriptor? Selected,
    IReadOnlyList<CapabilityDescriptor> Candidates);

public interface ICapabilityCatalog
{
    IReadOnlyList<CapabilityDescriptor> Snapshot();
}

[Alias("digitalbrain.capability-catalog-projection-grain.v1")]
public interface ICapabilityCatalogProjectionGrain : IGrainWithIntegerKey
{
    [Alias("read")]
    Task<CapabilityDescriptor[]> ReadAsync();
}

public static class CapabilityCatalogProjectionGrainIds
{
    public const long Singleton = 0;
}

public interface ICapabilityDescriptorSource
{
    IReadOnlyList<CapabilityDescriptor> Descriptors { get; }
}

public interface ICapabilityResolver
{
    Task<CapabilityResolution> ResolveAsync(
        CapabilitySearchRequest request,
        CancellationToken cancellationToken = default);
}
