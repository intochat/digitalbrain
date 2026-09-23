using System.Security.Cryptography;
using System.Text.Json;

namespace IntoChat;

internal static class BehaviorToolScope
{
    public static string Key(string scope, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
        { throw new ArgumentException("Use a behavior ID containing 1–100 ASCII letters, digits, hyphens or underscores.", nameof(id)); }
        return "behavior-" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { scope, id })));
    }
}