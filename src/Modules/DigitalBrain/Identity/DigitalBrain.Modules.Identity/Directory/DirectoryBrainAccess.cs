using DigitalBrain.Core.Enforcement;
using Orleans;

namespace DigitalBrain.Identity.Directory;

// The directory-backed answer to the brain membership question asked by HTTP route filters.
internal sealed class DirectoryBrainAccess(IGrainFactory grains) : IBrainAccess
{
    public ValueTask<bool> CanAccessAsync(string principalId, string brainId, CancellationToken cancellationToken = default)
        => new(grains.GetGrain<IIdentityDirectory>(IdentityGrains.Directory)
            .CanAccessAsync(principalId, brainId, cancellationToken));
}
