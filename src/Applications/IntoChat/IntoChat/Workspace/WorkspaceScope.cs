using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Core.Enforcement;

namespace IntoChat.Workspace;

internal sealed record WorkspaceScope(string Id, string Owner, string WorkspaceId)
{
    public static WorkspaceScope Current(BasicAuthOptions auth, string workspaceId)
        => Create(CallerContextStamper.TryGet(out var caller) && caller is not null ? caller.AccountId
            : auth.Username is { Length: > 0 } owner ? owner : AccountSession.DefaultLogin, workspaceId);

    public static WorkspaceScope Create(string owner, string workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId) || workspaceId.Length > 200 || workspaceId.Any(c => char.IsControl(c) || c is '/' or '\\'))
        { throw new ArgumentException("A workspace ID must contain 1–200 characters without path separators.", nameof(workspaceId)); }
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(owner + "\0" + workspaceId));
        return new("workspace-" + Convert.ToHexStringLower(digest), owner, workspaceId);
    }
}
