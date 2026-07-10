using DigitalBrain.Kernel.Abstractions;

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
        app.MapGet("/oauth/callback/{provider}", async (
            string provider,
            HttpRequest request,
            IServiceProvider services) =>
        {
            if (!Providers.Contains(provider)) return Results.NotFound();

            var connector = services.GetRequiredKeyedService<IConnector>(provider);
            var callback = new OAuthCallback(
                Code: request.Query["code"].FirstOrDefault() ?? string.Empty,
                State: request.Query["state"].FirstOrDefault() ?? string.Empty,
                Error: request.Query["error"].FirstOrDefault(),
                ErrorDescription: request.Query["error_description"].FirstOrDefault());
            var result = await connector.CompleteAuthAsync(callback, request.HttpContext.RequestAborted);

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
}
