using DigitalBrain.Core.Enforcement;
using DigitalBrain.Identity.Configuration;

namespace DigitalBrain.Identity;

// Temporary shim; deleted in Task 11.
public static class WorkspaceScope
{
    public static BrainScope Create(string owner, string name) => BrainScope.Create(owner, name);
    public static bool IsValidId(string? value) => BrainScope.IsValidId(value);

    public static BrainScope Current(BasicAuthOptions auth, string workspaceId)
        => Create(CallerContextStamper.TryGet(out var caller) ? caller.AccountId
            : auth.Username is { Length: > 0 } owner ? owner : AccountSession.DefaultLogin, workspaceId);
}
