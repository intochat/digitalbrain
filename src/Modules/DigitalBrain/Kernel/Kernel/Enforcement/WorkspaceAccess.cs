namespace DigitalBrain.Core.Enforcement;

// Modules that own workspace-scoped HTTP routes ask this seam whether the stamped principal may
// reach a workspace. IntoChat registers the directory-backed implementation.
public interface IWorkspaceAccess
{
    ValueTask<bool> CanAccessAsync(string principalId, string workspaceId, CancellationToken cancellationToken = default);
}
