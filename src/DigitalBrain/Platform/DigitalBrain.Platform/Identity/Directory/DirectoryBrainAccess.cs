using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Identity.Authority;
using Orleans;

namespace DigitalBrain.Platform.Identity.Directory;

// The directory-backed answer to the brain membership question asked by HTTP route filters.
internal sealed class DirectoryBrainAccess(IGrainFactory grains) : IBrainAccess
{
    public ValueTask<bool> CanAccessAsync(string principalId, string accountId, string brainId, CancellationToken cancellationToken = default)
        => new(HasMembership(principalId, accountId, brainId, cancellationToken));
    private async Task<bool> HasMembership(string principalId, string accountId, string brainId, CancellationToken ct)
        => await grains.GetGrain<IBrainAuthority>(BrainScope.Create(accountId, brainId).Id).Membership(principalId).WaitAsync(ct) is not null;

}

// Composed only in the Open posture: the synthetic owner IS the single tenant, while a real
// principal (a cookie session in development) still answers to the directory.
internal sealed class OpenOwnerBrainAccess(DirectoryBrainAccess directory) : IBrainAccess
{
    public ValueTask<bool> CanAccessAsync(string principalId, string accountId, string brainId, CancellationToken cancellationToken = default)
        => accountId == AccountSession.DefaultLogin && string.Equals(principalId, AccountSession.DefaultLogin, StringComparison.Ordinal)
            ? new(true)
            : directory.CanAccessAsync(principalId, accountId, brainId, cancellationToken);
}
