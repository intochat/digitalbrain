using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Salesforce;

// Credential validation remains lazy so an unconfigured module can start.
// Lazy like GmailOAuthConfiguration: an unconfigured Salesforce module must not fail silo
// startup, only refuse a login challenge or token refresh until it is configured.
internal sealed class SalesforceOAuthConfiguration(IOptions<SalesforceOAuthOptions> options)
{
    internal SalesforceOAuthConfiguration(IConfiguration configuration)
        : this(Options.Create(configuration.GetSection(SalesforceOAuthOptions.SectionName).Get<SalesforceOAuthOptions>() ?? new())) { }

    internal const string Section = SalesforceModule.OAuthConfigurationRoot;

    internal string ConsumerKey => options.Value.ConsumerKey ?? "";
    internal string ConsumerSecret => options.Value.ConsumerSecret ?? "";
    internal bool IsConfigured => IsValidCredential(ConsumerKey) && IsValidCredential(ConsumerSecret) && TryOrigin(out _);
    internal Uri? PublicOrigin => TryOrigin(out var origin) ? origin : null;

    internal static Uri AuthorizationEndpoint => new("https://login.salesforce.com/services/oauth2/authorize");
    internal static Uri TokenEndpoint => new("https://login.salesforce.com/services/oauth2/token");

    internal void RequireConfigured()
    {
        if (!IsConfigured)
        {
            throw new SalesforceUnavailableException(
                "Salesforce setup is incomplete. Configure the kernel Salesforce OAuth ConsumerKey, ConsumerSecret and PublicOrigin privately in Aspire.");
        }
    }

    internal void Configure(OAuthOptions options)
    {
        // Placeholders avoid startup failure; RequireConfigured guards any real challenge.
        options.ClientId = IsConfigured ? ConsumerKey : "not-configured";
        options.ClientSecret = IsConfigured ? ConsumerSecret : "not-configured";
        options.AuthorizationEndpoint = AuthorizationEndpoint.AbsoluteUri;
        options.TokenEndpoint = TokenEndpoint.AbsoluteUri;
    }

    internal HttpRequestMessage RefreshRequest(string refreshToken)
    {
        RequireConfigured();
        return new(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = ConsumerKey,
                ["client_secret"] = ConsumerSecret,
                ["refresh_token"] = refreshToken,
            }),
        };
    }

    private static bool IsValidCredential(string value) => !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);

    private bool TryOrigin(out Uri? origin)
    {
        if (Uri.TryCreate(options.Value.PublicOrigin, UriKind.Absolute, out origin)
            && (origin.Scheme == Uri.UriSchemeHttps || (origin.Scheme == Uri.UriSchemeHttp && origin.IsLoopback))
            && origin.UserInfo.Length == 0 && origin.Query.Length == 0 && origin.Fragment.Length == 0
            && origin.AbsolutePath == "/")
        {
            return true;
        }

        origin = null;
        return false;
    }
}
