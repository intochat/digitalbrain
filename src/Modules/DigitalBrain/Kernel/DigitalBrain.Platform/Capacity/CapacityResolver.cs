using DigitalBrain.Sdk.Capacity;

namespace DigitalBrain.Platform.Capacity;

// Resolution order is the platform's law: brain account (future) -> configured source -> provisioner.
internal sealed class CapacityResolver(
    IEnumerable<ICapacityConfiguredSource> sources,
    IEnumerable<ICapacityProvisioner> provisioners) : ICapacity
{
    public async ValueTask<ResolvedCapacity> Resolve(string kind, CapacityScope scope, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(scope);
        // Last registration wins, matching DI override convention (a test fake replaces the module's).
        if (sources.LastOrDefault(source => source.Kind == kind) is { } configured)
        { return new(kind, configured.Origin); }
        return await Provision(kind, scope, ct);
    }

    public async ValueTask<ResolvedCapacity> Provision(string kind, CapacityScope scope, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(scope);
        if (provisioners.LastOrDefault(provisioner => provisioner.Kind == kind) is { } provisioner)
        { return new(kind, await provisioner.Ensure(scope, ct)); }
        throw new CapacityUnavailableException();
    }
}
