using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Brain.Modules.Google;

public sealed record GoogleOptions(
    string ClientId,
    string ClientSecret,
    string RedirectUri,
    TimeSpan RequestTimeout)
{
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);

    public static GoogleOptions? FromConfiguration(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var clientId = configuration["DigitalBrain:Google:ClientId"];
        var clientSecret = configuration["DigitalBrain:Google:ClientSecret"];
        var redirectUri = configuration["DigitalBrain:Google:RedirectUri"];
        var supplied = new[] { clientId, clientSecret, redirectUri }
            .Count(value => !string.IsNullOrWhiteSpace(value));

        if (supplied == 0)
            return null;
        if (supplied != 3)
            throw new InvalidOperationException("Google OAuth configuration must be either complete or absent.");
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var redirect))
            throw new InvalidOperationException("Google OAuth redirect URI must be absolute.");
        if (environment.IsProduction() && redirect.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Google OAuth redirect URI must use HTTPS in Production.");

        return new GoogleOptions(clientId!, clientSecret!, redirectUri!, DefaultRequestTimeout);
    }
}
