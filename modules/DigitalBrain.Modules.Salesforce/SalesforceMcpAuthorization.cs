using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Authentication;

namespace DigitalBrain.Salesforce;

internal sealed class SalesforceMcpAuthorization(IConfiguration configuration) : ISalesforceMcpAuthorization
{
    private const string Root = "DigitalBrain:Salesforce";

    public ClientOAuthOptions CreateOptions(ITokenCache tokenCache) => new()
    {
        ClientId = Required("ClientId"),
        ClientSecret = Required("ClientSecret"),
        RedirectUri = RequiredUri("RedirectUri"),
        Scopes = ["mcp_api", "refresh_token"],
        TokenCache = tokenCache ?? throw new ArgumentNullException(nameof(tokenCache)),
        AuthorizationRedirectDelegate = LoopbackAuthorization.AuthorizeAsync,
    };

    private string Required(string name) => configuration[$"{Root}:{name}"]
        ?? throw new InvalidOperationException(
            $"Salesforce requires {Root}:{name}. Configure it through SalesforceModule.WithSalesforce() in AppHost.");

    private Uri RequiredUri(string name)
    {
        var value = Required(name);

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException($"{Root}:{name} must be an absolute URI.");
    }

    private static class LoopbackAuthorization
    {
        internal static async Task<string?> AuthorizeAsync(
            Uri authorizationUri,
            Uri redirectUri,
            CancellationToken cancellationToken)
        {
            if (!redirectUri.IsLoopback || redirectUri.Scheme != Uri.UriSchemeHttp)
            {
                throw new InvalidOperationException(
                    "Salesforce OAuth requires an HTTP loopback RedirectUri for the local Aspire profile.");
            }

            using var listener = new HttpListener();
            listener.Prefixes.Add($"{redirectUri.Scheme}://{redirectUri.Host}:{redirectUri.Port}/");
            listener.Start();

            Process.Start(new ProcessStartInfo(authorizationUri.AbsoluteUri) { UseShellExecute = true });

            var context = await listener.GetContextAsync().WaitAsync(cancellationToken);
            var code = context.Request.QueryString["code"];
            var response = Encoding.UTF8.GetBytes(
                code is null
                    ? "DigitalBrain could not complete Salesforce authorization. You can close this window."
                    : "DigitalBrain Salesforce authorization completed. You can close this window.");
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.ContentLength64 = response.Length;
            await context.Response.OutputStream.WriteAsync(response, cancellationToken);
            context.Response.Close();

            return code;
        }
    }
}
