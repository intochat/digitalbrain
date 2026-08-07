using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Google.Auth;

internal static class GoogleOAuthOptions
{
    internal const string ConfigurationRoot = "DigitalBrain:Google:Gmail";

    internal static GoogleOAuthClientSettings Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new GoogleOAuthClientSettings(
            Required(configuration, "ClientId"),
            Required(configuration, "ClientSecret"),
            RequiredUri(configuration, "RedirectUri"));
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
