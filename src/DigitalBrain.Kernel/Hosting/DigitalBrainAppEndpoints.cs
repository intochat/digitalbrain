using DigitalBrain.Core;
using DigitalBrain.Google;
using DigitalBrain.Kernel.Abstractions;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.Salesforce;
using Orleans;

namespace DigitalBrain.Kernel.Hosting;

public static class DigitalBrainAppEndpoints
{
    public static WebApplication MapConnectorOAuthCallbacks(this WebApplication app)
    {
        app.MapGet("/oauth/start/{provider}", async (
            string provider,
            HttpRequest request,
            IServiceProvider services) =>
        {
            SetOAuthResponseHeaders(request.HttpContext.Response);
            var target = (request.Path.Value ?? string.Empty) + (request.QueryString.Value ?? string.Empty);
            if (!OAuthCallbackPaths.IsSupportedProvider(provider) ||
                !OAuthCallbackPaths.TryParseInternalStartPath(target, provider, out var flowReference))
                return Results.StatusCode(StatusCodes.Status400BadRequest);

            var protector = services.GetRequiredService<IOAuthStateProtector>();
            if (!protector.TryUnprotect(flowReference, out var owner))
                return Results.StatusCode(StatusCodes.Status400BadRequest);

            var cluster = services.GetRequiredService<IClusterClient>();
            using var startDeadline = CreateServerOperationDeadline(services);
            if (string.Equals(provider, OAuthCallbackPaths.GoogleProvider, StringComparison.Ordinal))
            {
                var googleResult = await cluster
                    .GetGrain<IGmailReadToolGrain>(owner.Value)
                    .BeginAuthorizationAsync(flowReference, startDeadline.Token);
                return googleResult.Status == GmailReadStatus.NeedsAuth &&
                       GoogleClientFactory.IsAllowedAuthorizationUrl(googleResult.ConnectionUrl)
                    ? Results.Redirect(googleResult.ConnectionUrl!, permanent: false, preserveMethod: false)
                    : Results.StatusCode(StatusCodes.Status400BadRequest);
            }

            var salesforceResult = await cluster
                .GetGrain<ISalesforceReadToolGrain>(owner.Value)
                .BeginAuthorizationAsync(flowReference, startDeadline.Token);
            return salesforceResult.Status == SalesforceReadStatus.NeedsAuth &&
                   SalesforceClientFactory.IsAllowedAuthorizationUrl(salesforceResult.ConnectionUrl)
                ? Results.Redirect(salesforceResult.ConnectionUrl!, permanent: false, preserveMethod: false)
                : Results.StatusCode(StatusCodes.Status400BadRequest);
        });

        app.MapGet("/oauth/callback/{provider}", async (
            string provider,
            HttpRequest request,
            IServiceProvider services) =>
        {
            SetOAuthResponseHeaders(request.HttpContext.Response);
            if (!OAuthCallbackPaths.IsSupportedProvider(provider)) return Results.NotFound();

            var callback = new OAuthCallback(
                Code: request.Query["code"].FirstOrDefault() ?? string.Empty,
                State: request.Query["state"].FirstOrDefault() ?? string.Empty,
                Error: request.Query["error"].FirstOrDefault(),
                ErrorDescription: request.Query["error_description"].FirstOrDefault());
            AuthResult result;
            if (string.Equals(provider, OAuthCallbackPaths.SalesforceProvider, StringComparison.Ordinal))
            {
                var protector = services.GetRequiredService<IOAuthStateProtector>();
                if (!protector.TryUnprotect(callback.State, out var owner))
                {
                    result = new AuthResult(false, "invalid-state");
                }
                else
                {
                    var cluster = services.GetRequiredService<IClusterClient>();
                    using var completionDeadline = CreateServerOperationDeadline(services);
                    result = await cluster
                        .GetGrain<ISalesforceReadToolGrain>(owner.Value)
                        .CompleteAuthorizationAsync(callback, completionDeadline.Token);
                }
            }
            else
            {
                var protector = services.GetRequiredService<IOAuthStateProtector>();
                if (!protector.TryUnprotect(callback.State, out var owner))
                {
                    result = new AuthResult(false, "invalid-state");
                }
                else
                {
                    var cluster = services.GetRequiredService<IClusterClient>();
                    using var completionDeadline = CreateServerOperationDeadline(services);
                    result = await cluster
                        .GetGrain<IGmailReadToolGrain>(owner.Value)
                        .CompleteAuthorizationAsync(callback, completionDeadline.Token);
                }
            }

            var title = result.Success ? "Connection complete" : "Connection not completed";
            var message = result.Success
                ? "You can return to DigitalBrain. INO will resume your request automatically."
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

    private static CancellationTokenSource CreateServerOperationDeadline(IServiceProvider services)
    {
        var lifetime = services.GetRequiredService<IHostApplicationLifetime>();
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        return deadline;
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
