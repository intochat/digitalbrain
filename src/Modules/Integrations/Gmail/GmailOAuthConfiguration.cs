using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Integrations.Gmail;

internal sealed class GmailOAuthConfiguration(IConfiguration configuration)
{
    internal const string Root = "DigitalBrain:Integrations:Gmail:OAuth";
    internal const string ReadScope = "https://www.googleapis.com/auth/gmail.readonly";
    internal const string ComposeScope = "https://www.googleapis.com/auth/gmail.compose";
    internal string ClientId => configuration[$"{Root}:ClientId"] ?? "";
    internal string ClientSecret => configuration[$"{Root}:ClientSecret"] ?? "";
    internal bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret)
        && TryOrigin(out _);
    internal Uri PublicOrigin => TryOrigin(out var origin) ? origin! : throw new GmailOperationException("Gmail setup is incomplete. Configure the kernel Gmail OAuth ClientId, ClientSecret and PublicOrigin privately in Aspire.");

    private bool TryOrigin(out Uri? origin)
    {
        if (Uri.TryCreate(configuration[$"{Root}:PublicOrigin"], UriKind.Absolute, out origin)
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
            throw new GmailOperationException("Gmail setup is incomplete. Configure the kernel Gmail OAuth ClientId, ClientSecret and PublicOrigin privately in Aspire.");
        }
    }
}

internal sealed class GmailAuthenticationRequiredException : Exception
{
    public GmailAuthenticationRequiredException() : base("Gmail authorization is required.") { }
}

internal sealed class GmailOperationException(string message) : Exception(message);
