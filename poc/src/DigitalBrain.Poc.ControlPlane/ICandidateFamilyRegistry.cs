using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.ControlPlane;

public interface ICandidateFamilyRegistry
{
    ValueTask<bool> TryReserveAsync(
        AuthenticatedPrincipal owner,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default);

    ValueTask<bool> IsReservedForAsync(
        AuthenticatedPrincipal owner,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default);
}
