using DigitalBrain.Kernel.Contracts;
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
    int MaximumMatches = 3,
    IReadOnlyList<CapabilityDescriptor>? Descriptors = null);

public sealed record CapabilityResolution(
    CapabilityResolutionReceipt Receipt,
    CapabilityDescriptor? Selected,
    IReadOnlyList<CapabilityDescriptor> Candidates);

public sealed record FeatureCapabilityBinding(
    BrainOwnerId OwnerId,
    ActorId ActorId,
    FeatureInstallationId InstallationId,
    ReleaseDigest Release,
    GrantRevision GrantRevision,
    string InputKind,
    long PublicationFence,
    string AuthorityDigest,
    string AccessDigest,
    CapabilityConnectionBinding[] RequiredConnections);

public sealed record CapabilityConnectionBinding(
    string Provider,
    ProviderConnectionId? ConnectionId);

public sealed record CapabilityCatalogEntry(
    CapabilityDescriptor Descriptor,
    FeatureCapabilityBinding? Feature = null);

public sealed record OwnerCapabilityCatalogSnapshot(
    IReadOnlyList<CapabilityCatalogEntry> Entries,
    IReadOnlySet<string> HealthyConnections)
{
    public IReadOnlyList<CapabilityDescriptor> Descriptors => Entries.Select(static entry => entry.Descriptor).ToArray();

    public CapabilityCatalogEntry? Bind(CapabilityDescriptor descriptor) => Entries.SingleOrDefault(entry =>
        string.Equals(entry.Descriptor.Id, descriptor.Id, StringComparison.Ordinal) &&
        entry.Descriptor.Version == descriptor.Version &&
        string.Equals(entry.Descriptor.Name, descriptor.Name, StringComparison.Ordinal) &&
        string.Equals(entry.Descriptor.Description, descriptor.Description, StringComparison.Ordinal) &&
        entry.Descriptor.Examples.SequenceEqual(descriptor.Examples, StringComparer.Ordinal) &&
        entry.Descriptor.RequiredGrants.SequenceEqual(descriptor.RequiredGrants, StringComparer.Ordinal) &&
        entry.Descriptor.RequiredConnections.SequenceEqual(descriptor.RequiredConnections, StringComparer.Ordinal) &&
        entry.Descriptor.Origin == descriptor.Origin &&
        entry.Descriptor.Kind == descriptor.Kind &&
        entry.Descriptor.Available == descriptor.Available);
}

public interface IOwnerCapabilityCatalog
{
    Task<OwnerCapabilityCatalogSnapshot> ReadAsync(
        BrainOwnerId ownerId,
        ActorId actorId,
        CancellationToken cancellationToken = default);
}

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

public enum OwnerConnectionHealthStatus
{
    Unspecified = 0,
    Healthy = 1,
    NeedsReauth = 2,
    Disconnected = 3,
    Misconfigured = 4
}

[GenerateSerializer, Alias("digitalbrain.owner-connection-snapshot.v1")]
public sealed record OwnerConnectionSnapshot(
    [property: Id(0)] string Provider,
    [property: Id(1)] string ConnectionId,
    [property: Id(2)] string DisplayName,
    [property: Id(3)] OwnerConnectionHealthStatus Health,
    [property: Id(4)] string? HealthDetail,
    [property: Id(5)] string[] UnlockedCapabilityIds,
    [property: Id(6)] string? ConnectPath);

[Alias("digitalbrain.owner-connection-catalog-grain.v1")]
public interface IOwnerConnectionCatalogGrain : IGrainWithStringKey
{
    [Alias("read")]
    Task<OwnerConnectionSnapshot[]> ReadAsync();
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
