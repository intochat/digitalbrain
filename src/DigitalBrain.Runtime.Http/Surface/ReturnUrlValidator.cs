using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Runtime.Http.Surface;

// A local path ("/x", never "//x" or "/\x"), or an https URL on one of the allowed origins without user info.
public sealed class ReturnUrlValidator(IEnumerable<string> allowedOrigins)
{
    private readonly HashSet<string> _origins = allowedOrigins
        .Select(origin => origin.TrimEnd('/'))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Validates raw return URL strings including relative paths and malformed inputs.")]
    public bool IsAllowed(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || returnUrl.Any(char.IsControl))
        {
            return false;
        }

        if (returnUrl[0] == '/')
        {
            return returnUrl.Length == 1 || (returnUrl[1] != '/' && returnUrl[1] != '\\');
        }

        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        return _origins.Contains(uri.GetLeftPart(UriPartial.Authority));
    }
}
