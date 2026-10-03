using DigitalBrain.Contracts.Enforcement;

namespace DigitalBrain.Sdk.Identity;

public interface IIdentity
{
    CallerContext? CurrentPrincipal { get; }

    string RequireOwner();

    ValueTask<bool> CanAccessAsync(string principalId, string brainId, CancellationToken cancellationToken = default);

    ValueTask<bool> HasGrantAsync(CallerContext caller, string semanticTypeId, CancellationToken cancellationToken = default);
}
