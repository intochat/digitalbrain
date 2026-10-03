using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Contracts.Identity;
using Orleans;

namespace DigitalBrain.Platform.Identity.Directory;

// The directory-backed answer to the brain membership question asked by HTTP route filters.
internal sealed class DirectoryBrainAccess(IGrainFactory grains) : IBrainAccess
{
    public ValueTask<bool> CanAccessAsync(string principalId, string brainId, CancellationToken cancellationToken = default)
        => new(grains.GetGrain<IIdentityDirectory>(IdentityGrains.Directory)
            .CanAccessAsync(principalId, brainId, cancellationToken));
}

// Composed only in the Open posture: the synthetic owner IS the single tenant, while a real
// principal (a cookie session in development) still answers to the directory.
internal sealed class OpenOwnerBrainAccess(DirectoryBrainAccess directory) : IBrainAccess
{
    public ValueTask<bool> CanAccessAsync(string principalId, string brainId, CancellationToken cancellationToken = default)
        => string.Equals(principalId, AccountSession.DefaultLogin, StringComparison.Ordinal)
            ? new(true)
            : directory.CanAccessAsync(principalId, brainId, cancellationToken);
}
