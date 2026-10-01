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
        if (sources.FirstOrDefault(source => source.Kind == kind) is { } configured)
        { return new(kind, configured.Origin); }
        return await Provision(kind, scope, ct);
    }

    public async ValueTask<ResolvedCapacity> Provision(string kind, CapacityScope scope, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(scope);
        if (provisioners.FirstOrDefault(provisioner => provisioner.Kind == kind) is { } provisioner)
        { return new(kind, await provisioner.Ensure(scope, ct)); }
        throw new CapacityUnavailableException();
    }
}
