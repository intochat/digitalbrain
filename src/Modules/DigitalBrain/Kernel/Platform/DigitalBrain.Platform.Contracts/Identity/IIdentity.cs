using DigitalBrain.Contracts.Enforcement;

namespace DigitalBrain.Platform.Contracts.Identity;

public interface IIdentity
{
    CallerContext? CurrentPrincipal { get; }

    string RequirePrincipal();

    ValueTask<bool> CanAccessAsync(string principalId, string accountId, string brainId, CancellationToken cancellationToken = default);

    ValueTask<bool> HasGrantAsync(CallerContext caller, string semanticTypeId, CancellationToken cancellationToken = default);
}
