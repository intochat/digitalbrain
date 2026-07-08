using System.Text.RegularExpressions;

namespace DigitalBrain.Ino;

internal static partial class SecretText
{
    public static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var redacted = SecretAssignmentRegex().Replace(value, "$1=[redacted]");
        return BearerTokenRegex().Replace(redacted, "$1 [redacted]");
    }

    [GeneratedRegex(@"(?i)\b(password|pass|secret|client_secret|refresh_token|access_token|api_key|apikey|token|key)\s*[:=]\s*[^,;\s}\]]+")]
    private static partial Regex SecretAssignmentRegex();

    [GeneratedRegex(@"(?i)\b(bearer)\s+[a-z0-9._\-]+")]
    private static partial Regex BearerTokenRegex();
}
