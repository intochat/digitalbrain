using System.Text.Json;

namespace DigitalBrain.Sdk;

// This scope is minted by an authenticated edge and carried in protected OAuth state.
// Callbacks must never infer the owner from the anonymous callback request.
public sealed record BrowserLoginWorkspace(string Registry)
{
    private const string Prefix = "workspace-login:";
    public string ToScope() => Prefix + JsonSerializer.Serialize(this);
    public static BrowserLoginWorkspace? FromScope(string? scope) =>
        scope?.StartsWith(Prefix, StringComparison.Ordinal) == true
            ? JsonSerializer.Deserialize<BrowserLoginWorkspace>(scope[Prefix.Length..]) : null;
}
