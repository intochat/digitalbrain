using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Sdk;
using DigitalBrain.Sdk.Connectors;
using DigitalBrain.Salesforce;
using Microsoft.Extensions.Options;
using DigitalBrain.Identity;

namespace IntoChat.Workspace;

internal static class WorkspaceConnectionsEndpoints
{
    internal static string Status(ConnectorStatus status) => status switch
    {
        ConnectorStatus.Connected => "Configured",
        ConnectorStatus.Expired => "Expired",
        ConnectorStatus.Disconnected => "Disconnected",
        _ => "Unavailable",
    };

    public static void MapWorkspaceConnections(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/workspaces/{workspaceId}/connections");
        group.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var id = http.Request.RouteValues["workspaceId"]?.ToString();
            if (!WorkspaceScope.IsValidId(id))
                { return Results.BadRequest(); }
            return await next(context);
        });
        group.AddEndpointFilter(WorkspaceAccessFilter.EnforceAsync);
        group.MapGet("", async (string workspaceId, IGrainFactory grains, IOptions<BasicAuthOptions> auth, IEnumerable<BrowserLogins> logins, CancellationToken ct) =>
        {
            var scope = WorkspaceScope.Current(auth.Value, workspaceId);
            var owner = CallerContextStamper.Require().PrincipalId;
            var rows = WorkspaceConnectionRecords.Combine(scope, owner, await grains.GetGrain<IConnectors>(scope.Id).List(ct),
                await grains.GetGrain<IConnectors>(owner).List(ct)).Select(Project).ToList();
            if (logins.Any(login => login.Definition.Provider == "salesforce"))
            {
                var connection = await grains.GetGrain<ISalesforce>(scope.Id).ReadConnection();
                if (connection.Connected) { rows.Add(ProjectSalesforce(connection)); }
            }
            return Results.Ok(rows);
        });
        group.MapPost("/connect", async (string workspaceId, ConnectRequest input, IGrainFactory grains, IOptions<BasicAuthOptions> auth, CancellationToken ct) =>
        {
            // The browser may supply a new value, never a reference into another owner's vault.
            if (input.SecretReference is not null) { return Results.BadRequest(new { error = "Supply a connection value." }); }
            if (string.IsNullOrWhiteSpace(input.ConnectionId) || input.ConnectionId.StartsWith("oauth:", StringComparison.Ordinal)) { return Results.BadRequest(); }
            try { return Results.Ok(Project(await Registry(grains, auth.Value, workspaceId).Connect(input, Caller(workspaceId), ct))); }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        });
        group.MapPost("/probe", async (string workspaceId, ConnectionAction input, IGrainFactory grains, IOptions<BasicAuthOptions> auth, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input.ConnectionId)) { return Results.BadRequest(); }
            if (input.ConnectionId == "oauth:salesforce")
            { return Results.Ok(ProjectSalesforce(await grains.GetGrain<ISalesforce>(WorkspaceScope.Current(auth.Value, workspaceId).Id).ReadConnection())); }
            try { return Results.Ok(Project(await (await RegistryFor(grains, auth.Value, workspaceId, input.ConnectionId, ct)).Probe(input.ConnectionId, Caller(workspaceId), ct))); }
            catch (ConnectorNotConfiguredException) { return Results.NotFound(); }
        });
        group.MapPost("/disconnect", async (string workspaceId, ConnectionAction input, IGrainFactory grains, IOptions<BasicAuthOptions> auth, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input.ConnectionId)) { return Results.BadRequest(); }
            if (input.ConnectionId == "oauth:salesforce")
            {
                await grains.GetGrain<ISalesforce>(WorkspaceScope.Current(auth.Value, workspaceId).Id).Disconnect(new());
                return Results.Ok();
            }
            await (await RegistryFor(grains, auth.Value, workspaceId, input.ConnectionId, ct)).Disconnect(input.ConnectionId, Caller(workspaceId), ct);
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
        group.MapPost("/services/{provider}/start", (string workspaceId, string provider, IEnumerable<BrowserLogins> logins, IOptions<BasicAuthOptions> auth) =>
        {
            var login = logins.FirstOrDefault(value => value.Definition.Provider == provider);
            if (login is null) { return Results.NotFound(); }
            if (provider != "salesforce") { return Results.Json(new { error = "This provider requires a service-specific setup flow." }, statusCode: 503); }
            try { return Results.Ok(new { url = login.Require(new BrowserLoginWorkspace(WorkspaceScope.Current(auth.Value, workspaceId).Id).ToScope()).AbsoluteUri }); }
            catch (InvalidOperationException) { return Results.Json(new { error = "Provider authorization is unavailable. Check the deployment configuration." }, statusCode: 503); }
        });
    }

    private static IConnectors Registry(IGrainFactory grains, BasicAuthOptions auth, string workspaceId) =>
        grains.GetGrain<IConnectors>(WorkspaceScope.Current(auth, workspaceId).Id);
    private static async Task<IConnectors> RegistryFor(IGrainFactory grains, BasicAuthOptions auth, string workspaceId, string id, CancellationToken ct)
    {
        var scope = WorkspaceScope.Current(auth, workspaceId);
        var current = grains.GetGrain<IConnectors>(scope.Id);
        if ((await current.List(ct)).Any(record => record.Id == id)) { return current; }
        var owner = CallerContextStamper.Require().PrincipalId;
        var legacy = grains.GetGrain<IConnectors>(owner);
        return WorkspaceConnectionRecords.Combine(scope, owner, [], await legacy.List(ct)).Any(record => record.Id == id) ? legacy : current;
    }
    private static CallerContext Caller(string workspaceId) => CallerContextStamper.Require() with { WorkspaceId = workspaceId };
    private static object Project(ConnectorRecord record) => new { record.Id, record.Source, status = Status(record.Status), record.LastProbedAt };
    private static object ProjectSalesforce(SalesforceConnection record) => new
    {
        id = "oauth:salesforce", source = "salesforce", account = record.InstanceUrl,
        status = !record.Connected ? "Disconnected" : record.ExpiresAt <= DateTimeOffset.UtcNow ? "Expired" : "Configured",
    };
    internal sealed record ConnectionAction(string? ConnectionId);
}
