using DigitalBrain.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Google.Auth;

internal static class GoogleOAuthOptions
{
    internal const string ConfigurationRoot = "DigitalBrain:Google:Gmail";

    internal static GoogleOAuthClientSettings Read(IConfiguration configuration, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var redirectUri = RequiredUri(configuration, "RedirectUri");
        if (!OAuthCallbackPaths.EndsWithCanonicalCallback(redirectUri))
        {
            var message =
                $"Gmail RedirectUri is '{redirectUri}' but the kernel serves OAuth callbacks at paths ending with "
                + $"'{OAuthCallbackPaths.RelativePath}'. Update configuration '{ConfigurationRoot}:RedirectUri' "
                + $"(and the Google OAuth client) so both end with '{OAuthCallbackPaths.RelativePath}'.";
            if (logger is not null)
            {
                logger.LogWarning("{Message}", message);
            }
            else
            {
                Console.Error.WriteLine($"warn: {message}");
            }
        }

        return new GoogleOAuthClientSettings(
            Required(configuration, "ClientId"),
            Required(configuration, "ClientSecret"),
            redirectUri);
    }

    private static string Required(IConfiguration configuration, string name)
    {
        var key = $"{ConfigurationRoot}:{name}";
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Gmail requires projected configuration '{key}'.");
        }

        RejectPlaceholder(key, value);
        return value;
    }

    private static Uri RequiredUri(IConfiguration configuration, string name)
    {
        var value = Required(configuration, name);
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException(
                $"{ConfigurationRoot}:{name} must be an absolute URI.");
    }

    private static void RejectPlaceholder(string key, string value)
    {
        if (string.Equals(value, "local-dev", StringComparison.Ordinal)
            || string.Equals(value, "local-dev-secret", StringComparison.Ordinal)
            || string.Equals(value, "http://localhost/oauth/callback", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Gmail is using a disallowed placeholder for '{key}'. Configure a real application credential.");
        }
    }
}

internal readonly record struct GoogleOAuthClientSettings(
    string ClientId,
    string ClientSecret,
    Uri RedirectUri);
