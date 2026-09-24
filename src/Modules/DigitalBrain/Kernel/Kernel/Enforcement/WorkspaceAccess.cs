namespace DigitalBrain.Core.Enforcement;

// Modules that own workspace-scoped HTTP routes ask this seam whether the stamped principal may
// reach a workspace, without depending on the Identity module. Identity registers the
// directory-backed implementation; the seam exists so the rule stays one implementation.
public interface IWorkspaceAccess
{
    ValueTask<bool> CanAccessAsync(string principalId, string workspaceId, CancellationToken cancellationToken = default);
}
