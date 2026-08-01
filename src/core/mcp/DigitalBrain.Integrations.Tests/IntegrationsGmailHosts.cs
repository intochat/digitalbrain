using System.Diagnostics.CodeAnalysis;
using System.Net;
using DigitalBrain.Google.Auth;

namespace DigitalBrain.Integrations.Tests;

internal static class IntegrationsGmailHosts
{
    internal const string ClientId = "integrations-gmail-client.apps.googleusercontent.com";
    internal const string ClientSecret = "integrations-gmail-client-secret";
    internal const string RedirectUri = "https://ui.test.digitalbrain.local/oauth/callback";
    internal const string AccessToken = "ya29.integrations-gmail-access";
    internal const string RefreshToken = "1//integrations-gmail-refresh";

    private static readonly object Gate = new();
    private static FakeGoogleTokenHost? _tokenHost;
    private static FakeGmailRestHost? _gmailHost;

    internal static FakeGoogleTokenHost TokenHost => Ensure().Token;

    internal static FakeGmailRestHost GmailHost => Ensure().Gmail;

    internal static void ApplyConfiguration(DigitalBrain.Testing.DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        var hosts = Ensure();
        brain.Configure($"{GoogleOAuthOptions.ConfigurationRoot}:ClientId", ClientId);
        brain.Configure($"{GoogleOAuthOptions.ConfigurationRoot}:ClientSecret", ClientSecret);
        brain.Configure($"{GoogleOAuthOptions.ConfigurationRoot}:RedirectUri", RedirectUri);
        brain.Configure($"{GoogleOAuthOptions.ConfigurationRoot}:TokenServerUrl", hosts.Token.TokenServerUrl);
        brain.Configure($"{GoogleOAuthOptions.ConfigurationRoot}:BaseUri", hosts.Gmail.BaseUri.AbsoluteUri);
    }

    internal static void ResetRuntimeState()
    {
        lock (Gate)
        {
            var hosts = EnsureUnlocked();
            hosts.Gmail.Clear();
            hosts.Token.ExchangeResponse = SuccessToken();
            hosts.Token.RefreshResponse = SuccessToken(includeRefresh: false);
            hosts.Token.ExchangeStatusCode = HttpStatusCode.OK;
            hosts.Token.ExchangeError = null;
            hosts.Token.RefreshStatusCode = HttpStatusCode.OK;
            hosts.Token.RefreshError = null;
            hosts.Gmail.GetStatusCode = HttpStatusCode.OK;
            hosts.Gmail.ListStatusCode = HttpStatusCode.OK;
            hosts.Gmail.GetErrorBody = null;
        }
    }

    private static (FakeGoogleTokenHost Token, FakeGmailRestHost Gmail) Ensure()
    {
        lock (Gate)
        {
            return EnsureUnlocked();
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Process-lifetime fake hosts are shared across Integrations Gmail scenarios.")]
    private static (FakeGoogleTokenHost Token, FakeGmailRestHost Gmail) EnsureUnlocked()
    {
        if (_tokenHost is not null && _gmailHost is not null)
        {
            return (_tokenHost, _gmailHost);
        }

        var tokenHost = FakeGoogleTokenHost.StartAsync().GetAwaiter().GetResult();
        var gmailHost = FakeGmailRestHost.StartAsync().GetAwaiter().GetResult();
        tokenHost.ExchangeResponse = SuccessToken();
        tokenHost.RefreshResponse = SuccessToken(includeRefresh: false);
        _tokenHost = tokenHost;
        _gmailHost = gmailHost;
        return (_tokenHost, _gmailHost);
    }

    private static object SuccessToken(bool includeRefresh = true)
    {
        if (!includeRefresh)
        {
            return new
            {
                access_token = AccessToken,
                token_type = "Bearer",
                expires_in = 3600,
            };
        }

        return new
        {
            access_token = AccessToken,
            refresh_token = RefreshToken,
            token_type = "Bearer",
            expires_in = 3600,
        };
    }
}
