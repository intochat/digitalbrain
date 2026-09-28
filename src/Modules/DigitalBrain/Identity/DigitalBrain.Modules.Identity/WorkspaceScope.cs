using DigitalBrain.Identity.Configuration;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Core.Enforcement;

namespace DigitalBrain.Identity;

public sealed record WorkspaceScope(string Id, string Owner, string WorkspaceId)
{
    public static WorkspaceScope Current(BasicAuthOptions auth, string workspaceId)
        => Create(CallerContextStamper.TryGet(out var caller) ? caller.AccountId
            : auth.Username is { Length: > 0 } owner ? owner : AccountSession.DefaultLogin, workspaceId);

    public static WorkspaceScope Create(string owner, string workspaceId)
    {
        if (!IsValidId(workspaceId))
        { throw new ArgumentException("A workspace ID must contain 1–200 characters without path separators.", nameof(workspaceId)); }
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(owner + "\0" + workspaceId));
        return new("workspace-" + Convert.ToHexStringLower(digest), owner, workspaceId);
    }

    // Workspace, thread and run ids travel in routes and grain keys: 1-200 characters, no control
    // characters and no path separators.
    public static bool IsValidId([NotNullWhen(true)] string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && !value.Any(c => char.IsControl(c) || c is '/' or '\\');
}
