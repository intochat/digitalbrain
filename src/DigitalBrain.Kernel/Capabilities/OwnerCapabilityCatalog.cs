using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.Kernel.Capabilities;

public interface IFeatureCapabilityProjectionSource
{
    Task<IReadOnlyList<FeatureCapabilityProjection>> ReadAsync(
        BrainOwnerId ownerId,
        ActorId actorId,
        CancellationToken cancellationToken = default);
}

internal sealed class FeatureCapabilityProjectionSource(IFeatureGrainResolver grains)
    : IFeatureCapabilityProjectionSource
{
    public async Task<IReadOnlyList<FeatureCapabilityProjection>> ReadAsync(
        BrainOwnerId ownerId,
        ActorId actorId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var projections = await grains.Hub(ownerId).ReadCapabilityCatalogAsync(actorId).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return projections;
    }
}

internal sealed class OwnerCapabilityCatalog(
    ICapabilityCatalog staticCatalog,
    IFeatureCapabilityProjectionSource featureSource,
    IOwnerConnectionHealth connectionHealth) : IOwnerCapabilityCatalog
{
    private const int MaximumNameLength = 80;
    private const int MaximumDescriptionLength = 2_048;
    private const int MaximumExampleLength = 256;
    private const int MaximumExamples = 8;
    private static readonly Regex Controls = new(@"\p{Cc}", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public async Task<OwnerCapabilityCatalogSnapshot> ReadAsync(
        BrainOwnerId ownerId,
        ActorId actorId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var staticDescriptors = staticCatalog.Snapshot().ToArray();
        var projections = await featureSource.ReadAsync(ownerId, actorId, cancellationToken).ConfigureAwait(false);
        var connections = staticDescriptors
            .SelectMany(static descriptor => descriptor.RequiredConnections.Select(static provider =>
                new CapabilityConnectionBinding(provider, null)))
            .Concat(projections.SelectMany(static projection => projection.Grants ?? [])
                .Where(static grant => !string.IsNullOrWhiteSpace(grant.Provider) && grant.ProviderConnectionId is not null)
                .Select(static grant => new CapabilityConnectionBinding(grant.Provider!, grant.ProviderConnectionId)))
            .Distinct()
            .ToArray();
        IReadOnlySet<CapabilityConnectionBinding> healthy;
        try
        {
            healthy = await connectionHealth.ReadHealthyAsync(ownerId, connections, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            healthy = new HashSet<CapabilityConnectionBinding>();
        }

        var entries = new List<CapabilityCatalogEntry>();
        foreach (var descriptor in staticDescriptors
            .Where(static descriptor => descriptor.Available)
            .Where(descriptor => descriptor.RequiredConnections.All(provider =>
                healthy.Contains(new CapabilityConnectionBinding(provider, null)))))
        {
            entries.Add(new CapabilityCatalogEntry(descriptor));
        }
        foreach (var projection in projections)
        {
            if (TryProject(ownerId, actorId, projection, healthy, out var entry))
                entries.Add(entry);
        }
        var ordered = entries
            .GroupBy(static entry => (entry.Descriptor.Id, entry.Descriptor.Version))
            .Select(static group => group.Single())
            .OrderBy(static entry => entry.Descriptor.Id, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Descriptor.Version)
            .ToArray();
        return new OwnerCapabilityCatalogSnapshot(
            ordered,
            healthy.Select(static connection => connection.Provider).ToHashSet(StringComparer.Ordinal));
    }

    private static bool TryProject(
        BrainOwnerId ownerId,
        ActorId actorId,
        FeatureCapabilityProjection projection,
        IReadOnlySet<CapabilityConnectionBinding> healthy,
        out CapabilityCatalogEntry entry)
    {
        entry = null!;
        if (projection.OwnerId != ownerId || projection.ActorId != actorId ||
            string.IsNullOrWhiteSpace(projection.Goal) ||
            string.IsNullOrWhiteSpace(projection.InputKind) || projection.InputKind.Length > 128 ||
            string.IsNullOrWhiteSpace(projection.AuthorityDigest) ||
            string.IsNullOrWhiteSpace(projection.AccessDigest) ||
            projection.PublicationFence < 1)
            return false;
        var grants = projection.Grants ?? [];
        if (grants.Any(static grant =>
            (grant.Provider is null) != (grant.ProviderConnectionId is null) ||
            grant.Provider is not null && string.IsNullOrWhiteSpace(grant.Provider)))
            return false;
        var connectionBindings = grants
            .Where(static grant => grant.Provider is not null && grant.ProviderConnectionId is not null)
            .Select(static grant => new CapabilityConnectionBinding(grant.Provider!, grant.ProviderConnectionId))
            .Distinct()
            .OrderBy(static connection => connection.Provider, StringComparer.Ordinal)
            .ThenBy(static connection => connection.ConnectionId!.Value.Value, StringComparer.Ordinal)
            .ToArray();
        if (connectionBindings.Any(connection => !healthy.Contains(connection)))
            return false;
        var requiredConnections = connectionBindings
            .Select(static connection => connection.Provider)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static provider => provider, StringComparer.Ordinal)
            .ToArray();
        var scenarios = projection.Scenarios ?? [];
        var name = Bounded(
            scenarios.Select(static scenario => scenario.Name).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))
                ?? projection.Goal,
            MaximumNameLength);
        var description = Bounded(
            string.Join(" ", new[] { projection.Goal }.Concat(scenarios.Select(static scenario =>
                $"{scenario.Name}: {scenario.When} -> {scenario.Then}"))),
            MaximumDescriptionLength);
        var examples = scenarios
            .Select(static scenario => scenario.When)
            .Where(static example => !string.IsNullOrWhiteSpace(example))
            .Select(example => Bounded(example, MaximumExampleLength))
            .Where(static example => example.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(MaximumExamples)
            .ToArray();
        if (name.Length == 0 || description.Length == 0)
            return false;
        var descriptor = new CapabilityDescriptor(
            FeatureDescriptorId(projection.InstallationId),
            1,
            name,
            description,
            examples,
            [],
            requiredConnections,
            CapabilityOrigin.Feature,
            CapabilityOperationKind.InternalWrite,
            true);
        var binding = new FeatureCapabilityBinding(
            projection.OwnerId,
            projection.ActorId,
            projection.InstallationId,
            projection.Release,
            projection.GrantRevision,
            projection.InputKind,
            projection.PublicationFence,
            projection.AuthorityDigest,
            projection.AccessDigest,
            connectionBindings);
        entry = new CapabilityCatalogEntry(descriptor, binding);
        return true;
    }

    internal static string FeatureDescriptorId(FeatureInstallationId installationId) =>
        "feature." + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(installationId.Value)));

    private static string Bounded(string value, int maximumLength)
    {
        var normalized = Whitespace.Replace(Controls.Replace(value, " "), " ").Trim();
        return normalized.Length > maximumLength ? normalized[..maximumLength] : normalized;
    }
}
