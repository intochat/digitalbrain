using DigitalBrain.Core.Enforcement;
using Orleans;

namespace IntoChat.Identity.Directory;

// The directory-backed answer to the workspace membership question asked by HTTP route filters.
internal sealed class DirectoryWorkspaceAccess(IGrainFactory grains) : IWorkspaceAccess
{
    public ValueTask<bool> CanAccessAsync(string principalId, string workspaceId, CancellationToken cancellationToken = default)
        => new(grains.GetGrain<IIdentityDirectory>(IdentityGrains.Directory)
            .CanAccessAsync(principalId, workspaceId, cancellationToken));
}
