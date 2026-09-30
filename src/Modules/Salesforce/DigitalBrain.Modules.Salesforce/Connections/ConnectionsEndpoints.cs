using DigitalBrain.Core.Enforcement;
using DigitalBrain.Sdk;
using DigitalBrain.Sdk.Connectors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Salesforce;

internal static class ConnectionsEndpoints
{
    internal static string Status(ConnectorStatus status) => status switch
    {
        ConnectorStatus.Connected => "Configured",
        ConnectorStatus.Expired => "Expired",
        ConnectorStatus.Disconnected => "Disconnected",
        _ => "Unavailable",
    };

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = BrainRoutes.Group(endpoints, "/connections");
        group.MapGet("", async (IGrainFactory grains, IEnumerable<BrowserLogins> logins, CancellationToken ct) =>
        {
            var scope = CurrentScope();
            var owner = CallerContextStamper.Require().PrincipalId;
            var rows = ScopedConnectorRecords.Combine(scope, owner, await grains.GetGrain<IConnectors>(scope.Id).List(ct),
                await grains.GetGrain<IConnectors>(owner).List(ct)).Select(Project).ToList();
            if (logins.Any(login => login.Definition.Provider == "salesforce"))
            {
                var connection = await grains.GetGrain<ISalesforce>(scope.Id).ReadConnection();
                if (connection.Connected) { rows.Add(ProjectSalesforce(connection)); }
            }
            return Results.Ok(rows);
        });
        group.MapPost("/connect", async (ConnectRequest input, IGrainFactory grains, CancellationToken ct) =>
        {
            // The browser may supply a new value, never a reference into another owner's vault.
            if (input.SecretReference is not null) { return Results.BadRequest(new { error = "Supply a connection value." }); }
            if (string.IsNullOrWhiteSpace(input.ConnectionId) || input.ConnectionId.StartsWith("oauth:", StringComparison.Ordinal)) { return Results.BadRequest(); }
            try { return Results.Ok(Project(await grains.GetGrain<IConnectors>(CurrentScope().Id).Connect(input, CallerContextStamper.Require(), ct))); }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        });
        group.MapPost("/probe", async (ConnectionAction input, IGrainFactory grains, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input.ConnectionId)) { return Results.BadRequest(); }
            if (input.ConnectionId == "oauth:salesforce")
            { return Results.Ok(ProjectSalesforce(await grains.GetGrain<ISalesforce>(BrainScope.CurrentId()).ReadConnection())); }
            try { return Results.Ok(Project(await (await RegistryFor(grains, input.ConnectionId, ct)).Probe(input.ConnectionId, CallerContextStamper.Require(), ct))); }
            catch (ConnectorNotConfiguredException) { return Results.NotFound(); }
        });
        group.MapPost("/disconnect", async (ConnectionAction input, IGrainFactory grains, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input.ConnectionId)) { return Results.BadRequest(); }
            if (input.ConnectionId == "oauth:salesforce")
            {
                await grains.GetGrain<ISalesforce>(BrainScope.CurrentId()).Disconnect(new());
                return Results.Ok();
            }
            await (await RegistryFor(grains, input.ConnectionId, ct)).Disconnect(input.ConnectionId, CallerContextStamper.Require(), ct);
            return Results.Ok();
        });
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

    private static BrainScope CurrentScope()
    {
        var caller = CallerContextStamper.Require();
        return BrainScope.Create(caller.AccountId, caller.BrainId);
    }

    private static async Task<IConnectors> RegistryFor(IGrainFactory grains, string connectionId, CancellationToken ct)
    {
        var scope = CurrentScope();
        var current = grains.GetGrain<IConnectors>(scope.Id);
        if ((await current.List(ct)).Any(record => record.Id == connectionId)) { return current; }
        var owner = CallerContextStamper.Require().PrincipalId;
        var legacy = grains.GetGrain<IConnectors>(owner);
        return ScopedConnectorRecords.Combine(scope, owner, [], await legacy.List(ct)).Any(record => record.Id == connectionId) ? legacy : current;
    }

    private static object Project(ConnectorRecord record) => new { record.Id, record.Source, status = Status(record.Status), record.LastProbedAt };

    private static object ProjectSalesforce(SalesforceConnection record) => new
    {
        id = "oauth:salesforce", source = "salesforce", account = record.InstanceUrl,
        status = !record.Connected ? "Disconnected" : record.ExpiresAt <= DateTimeOffset.UtcNow ? "Expired" : "Configured",
    };

    internal sealed record ConnectionAction(string? ConnectionId);
}
