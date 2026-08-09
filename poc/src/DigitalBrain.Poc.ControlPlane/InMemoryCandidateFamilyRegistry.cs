using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.ControlPlane;

public sealed class InMemoryCandidateFamilyRegistry : ICandidateFamilyRegistry
{
    private readonly Dictionary<CandidateFamilyId, string> _families = [];
    private readonly Lock _gate = new();

    public ValueTask<bool> TryReserveAsync(
        AuthenticatedPrincipal owner,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return ValueTask.FromResult(_families.TryAdd(family, owner.OwnerId));
        }
    }

    public ValueTask<bool> IsReservedForAsync(
        AuthenticatedPrincipal owner,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return ValueTask.FromResult(
                _families.TryGetValue(family, out var ownerId) &&
                string.Equals(ownerId, owner.OwnerId, StringComparison.Ordinal));
        }
    }
}
