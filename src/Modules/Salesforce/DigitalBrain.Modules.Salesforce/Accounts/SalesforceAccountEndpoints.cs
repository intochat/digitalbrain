using DigitalBrain.Kernel.AspNetCore;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Contracts.Integrations.Accounts;
using DigitalBrain.Sdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Salesforce;

// The neutral accounts surface lives in the Sdk; Salesforce contributes its OAuth grant as an
// external account and the browser-login discovery and start routes.
internal sealed class SalesforceExternalAccount : IExternalAccount
{
    public const string OAuthConnectionId = "oauth:salesforce";

    public string ConnectionId => OAuthConnectionId;

    public async Task<AccountRow?> ReadAsync(IGrainFactory grains, CancellationToken cancellationToken)
    {
        var record = await grains.GetGrain<ISalesforce>(BrainScope.CurrentId()).ReadConnection();
        return record.Connected ? Project(record) : null;
    }

    public async Task DisconnectAsync(IGrainFactory grains, CancellationToken cancellationToken)
        => await grains.GetGrain<ISalesforce>(BrainScope.CurrentId()).Disconnect(new());

    private static AccountRow Project(SalesforceConnection record) => new(
        OAuthConnectionId, "salesforce",
        record.ExpiresAt <= DateTimeOffset.UtcNow ? "Expired" : "Configured",
        Account: record.InstanceUrl);
}

internal static class SalesforceAccountEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = BrainRoutes.Group(endpoints, "/integrations/accounts");
        group.MapGet("/services", (IEnumerable<BrowserLogins> logins) => Results.Ok(logins.Select(login => new
        {
            id = login.Definition.Provider,
            name = login.Definition.Provider == "gmail" ? "Google" : login.Definition.DisplayName,
            available = login.IsConfigured && login.Definition.Provider == "salesforce",
            reason = login.Definition.Provider switch
            {
                "gmail" => "Gmail browser authorization is not available in this deployment. Use Advanced to store an existing Gmail credential.",
                "github" => "GitHub requires a repository-specific setup request.",
                _ => login.IsConfigured ? null : "Operator OAuth setup is required.",
            },
            capabilities = login.Definition.Provider == "gmail" ? "Gmail authorization only; Calendar and Drive are not included." : "Provider authorization",
        })));
        group.MapPost("/services/{provider}/start", (string provider, IEnumerable<BrowserLogins> logins) =>
        {
            var login = logins.FirstOrDefault(value => value.Definition.Provider == provider);
            if (login is null) { return Results.NotFound(); }
            if (provider != "salesforce") { return Results.Json(new { error = "This provider requires a service-specific setup flow." }, statusCode: 503); }
            try { return Results.Ok(new { url = login.Require(new BrowserLoginWorkspace(BrainScope.CurrentId()).ToScope()).AbsoluteUri }); }
            catch (InvalidOperationException) { return Results.Json(new { error = "Provider authorization is unavailable. Check the deployment configuration." }, statusCode: 503); }
        });
    }
}
