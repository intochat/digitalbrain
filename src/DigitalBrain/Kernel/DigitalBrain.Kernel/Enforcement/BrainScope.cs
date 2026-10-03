using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Kernel.Enforcement;

public sealed record BrainScope(string Id, string Owner, string Name)
{
    public static string CurrentId()
    {
        if (!CallerContextStamper.TryGet(out var caller))
        { throw new InvalidOperationException("No caller context is stamped on this call."); }
        return Create(caller.AccountId, caller.BrainId).Id;
    }

    public static BrainScope Create(string owner, string name)
    {
        if (!IsValidId(name))
        { throw new ArgumentException("A brain ID must contain 1–200 characters without path separators.", nameof(name)); }
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        // "workspace-" prefix is a stored artifact of the previous naming; renaming it means a data
        // migration and is explicitly deferred.
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(owner + "\0" + name));
        return new("workspace-" + Convert.ToHexStringLower(digest), owner, name);
    }

    // Brain, thread and run ids travel in routes and grain keys: 1-200 characters, no control
    // characters and no path separators.
    public static bool IsValidId([NotNullWhen(true)] string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && !value.Any(c => char.IsControl(c) || c is '/' or '\\');
}
