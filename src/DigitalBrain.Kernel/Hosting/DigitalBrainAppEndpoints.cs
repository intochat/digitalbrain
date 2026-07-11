using DigitalBrain.Core;
using DigitalBrain.Kernel.Abstractions;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.Salesforce;
using Orleans;

namespace DigitalBrain.Kernel.Hosting;

public static class DigitalBrainAppEndpoints
{
    private static readonly HashSet<string> Providers = new(StringComparer.OrdinalIgnoreCase)
    {
        "google",
        "salesforce"
    };

    public static WebApplication MapConnectorOAuthCallbacks(this WebApplication app)
    {
        app.MapGet(OAuthCallbackPaths.SalesforceStart, async (
            HttpRequest request,
            IServiceProvider services) =>
        {
            SetOAuthResponseHeaders(request.HttpContext.Response);
            var startToken = request.Query["t"].FirstOrDefault() ?? string.Empty;
            var protector = services.GetRequiredService<IOAuthStateProtector>();
            if (!protector.TryUnprotect(startToken, out var owner))
                return Results.StatusCode(StatusCodes.Status400BadRequest);

            var cluster = services.GetRequiredService<IClusterClient>();
            var result = await cluster
                .GetGrain<ISalesforceReadToolGrain>(owner.Value)
                .BeginAuthorizationAsync(startToken, request.HttpContext.RequestAborted);
            return result.Status == SalesforceReadStatus.NeedsAuth &&
                   SalesforceClientFactory.IsAllowedAuthorizationUrl(result.ConnectionUrl)
                ? Results.Redirect(result.ConnectionUrl!, permanent: false, preserveMethod: false)
                : Results.StatusCode(StatusCodes.Status400BadRequest);
        });

        app.MapGet("/oauth/callback/{provider}", async (
            string provider,
            HttpRequest request,
            IServiceProvider services) =>
        {
            SetOAuthResponseHeaders(request.HttpContext.Response);
            if (!Providers.Contains(provider)) return Results.NotFound();

            var callback = new OAuthCallback(
                Code: request.Query["code"].FirstOrDefault() ?? string.Empty,
                State: request.Query["state"].FirstOrDefault() ?? string.Empty,
                Error: request.Query["error"].FirstOrDefault(),
                ErrorDescription: request.Query["error_description"].FirstOrDefault());
            AuthResult result;
            if (string.Equals(provider, "salesforce", StringComparison.OrdinalIgnoreCase))
            {
                var protector = services.GetRequiredService<IOAuthStateProtector>();
                if (!protector.TryUnprotect(callback.State, out var owner))
                {
                    result = new AuthResult(false, "invalid-state");
                }
                else
                {
                    var cluster = services.GetRequiredService<IClusterClient>();
                    result = await cluster
                        .GetGrain<ISalesforceReadToolGrain>(owner.Value)
                        .CompleteAuthorizationAsync(callback, request.HttpContext.RequestAborted);
                }
            }
            else
            {
                var connector = services.GetRequiredKeyedService<IConnector>(provider);
                result = await connector.CompleteAuthAsync(callback, request.HttpContext.RequestAborted);
            }

            var title = result.Success ? "Connection complete" : "Connection not completed";
            var message = result.Success
                ? "You can return to DigitalBrain and retry your request."
                : result.Error switch
                {
                    "consent-denied" => "Consent was denied. No connection was created.",
                    "invalid-state" or "state-mismatch" or "no-pending" => "This authorization request is invalid or expired. Start again from DigitalBrain.",
                    "no-code" => "The authorization response was incomplete. Start again from DigitalBrain.",
                    _ => "The provider connection could not be completed. Start again from DigitalBrain."
                };
            return Results.Content(
                $"<html><body><h1>{title}</h1><p>{message}</p></body></html>",
                "text/html",
                statusCode: result.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
        });

        return app;
    }

    private static void SetOAuthResponseHeaders(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.Pragma = "no-cache";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
        response.Headers.XContentTypeOptions = "nosniff";
    }
}
