using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Integrations.Salesforce;

// Keep the framework's state, browser correlation and PKCE implementation. Add only a
// server-side one-use claim after correlation succeeds, before any authorization-code exchange.
internal sealed class SalesforceOAuthHandler(
    IOptionsMonitor<OAuthOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : OAuthHandler<OAuthOptions>(options, logger, encoder)
{
    private static readonly object VerifiedRequestKey = new();

    protected override bool ValidateCorrelationId(AuthenticationProperties properties)
    {
        if (!base.ValidateCorrelationId(properties)
            || !properties.Items.TryGetValue(SalesforceOAuthEndpoints.RequestKey, out var request)
            || !Context.RequestServices.GetRequiredService<SalesforceConnections>().TryClaimCallback(request))
        {
            return false;
        }
        Context.Items[VerifiedRequestKey] = request;
        return true;
    }

    // HttpContext survives exceptional framework failure paths where Properties is null.
    internal static string? VerifiedRequest(HttpContext context)
        => context.Items.TryGetValue(VerifiedRequestKey, out var value) ? value as string : null;
}
