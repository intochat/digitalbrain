using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Google;

internal sealed class GmailOAuthConfiguration(IOptions<GmailOAuthOptions> options)
{
    internal GmailOAuthConfiguration(IConfiguration configuration)
        : this(Options.Create(configuration.GetSection(GmailOAuthOptions.SectionName).Get<GmailOAuthOptions>() ?? new())) { }

    internal const string Root = GoogleModule.GmailOAuthConfigurationRoot;
    internal const string ReadScope = "https://www.googleapis.com/auth/gmail.readonly";
    internal const string ComposeScope = "https://www.googleapis.com/auth/gmail.compose";
    internal string ClientId => options.Value.ClientId ?? "";
    internal string ClientSecret => options.Value.ClientSecret ?? "";
    internal string TokenEndpoint => string.IsNullOrWhiteSpace(options.Value.TokenEndpoint)
        ? "https://oauth2.googleapis.com/token"
        : options.Value.TokenEndpoint;
    internal bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret)
        && TryOrigin(out _);
    internal Uri PublicOrigin => TryOrigin(out var origin) ? origin! : throw new GmailUnavailableException("Gmail setup is incomplete. Configure the kernel Gmail OAuth ClientId, ClientSecret and PublicOrigin privately in Aspire.");

    private bool TryOrigin(out Uri? origin)
    {
        if (Uri.TryCreate(options.Value.PublicOrigin, UriKind.Absolute, out origin)
            && (origin.Scheme == "https" || origin.Scheme == "http" && origin.IsLoopback)
            && origin.AbsolutePath == "/" && origin.Query.Length == 0 && origin.Fragment.Length == 0
            && origin.UserInfo.Length == 0)
        {
            return true;
        }

        origin = null;
        return false;
    }

    internal void RequireConfigured()
    {
        if (!IsConfigured)
        {
            throw new GmailUnavailableException("Gmail setup is incomplete. Configure the kernel Gmail OAuth ClientId, ClientSecret and PublicOrigin privately in Aspire.");
        }
    }
}