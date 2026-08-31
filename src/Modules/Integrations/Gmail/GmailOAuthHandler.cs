using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Integrations.Gmail;

internal sealed class GmailOAuthHandler(IOptionsMonitor<OpenIdConnectOptions> options, ILoggerFactory logger, HtmlEncoder htmlEncoder, UrlEncoder encoder)
    : OpenIdConnectHandler(options, logger, htmlEncoder, encoder)
{
    private static readonly object VerifiedRequestKey = new();
    protected override bool ValidateCorrelationId(AuthenticationProperties properties)
    {
        if (!base.ValidateCorrelationId(properties)
            || !properties.Items.TryGetValue(GmailOAuthEndpoints.RequestKey, out var request)
            || !Context.RequestServices.GetRequiredService<GmailPendingActions>().TryClaim(request))
        {
            return false;
        }

        Context.Items[VerifiedRequestKey] = request;
        return true;
    }
    internal static string? VerifiedRequest(HttpContext context)
        => context.Items.TryGetValue(VerifiedRequestKey, out var request) ? request as string : null;
}
