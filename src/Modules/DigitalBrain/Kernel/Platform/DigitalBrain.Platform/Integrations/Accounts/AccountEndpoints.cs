using DigitalBrain.Sdk.Integrations.Accounts;
using DigitalBrain.Platform.Contracts.Integrations.Accounts;
using DigitalBrain.Kernel.AspNetCore;
using DigitalBrain.Kernel.Enforcement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Platform.Integrations.Accounts;

internal static class AccountEndpoints
{
    internal static string Status(AccountStatus status) => status switch
    {
        AccountStatus.Connected => "Configured",
        AccountStatus.Expired => "Expired",
        AccountStatus.Disconnected => "Disconnected",
        _ => "Unavailable",
    };

    internal static void MapAccounts(this IEndpointRouteBuilder endpoints)
    {
        var group = BrainRoutes.Group(endpoints, "/integrations/accounts");
        group.MapGet("", async (IGrainFactory grains, IEnumerable<IExternalAccount> external, CancellationToken cancellationToken) =>
        {
            var scope = CurrentScope();
            var rows = ScopedAccounts.Visible(scope, await grains.GetGrain<IIntegrationAccounts>(scope.Id).List(cancellationToken))
                .Select(Project).ToList();
            foreach (var account in external)
            {
                if (await account.ReadAsync(grains, cancellationToken) is { } row) { rows.Add(row); }
            }

            return Results.Ok(rows);
        });

        group.MapGet("/requests", async (IGrainFactory grains) =>
            Results.Ok(await grains.GetGrain<DigitalBrain.Contracts.Integrations.IIntegrationAccounts>(CurrentScope().Id).ListPending()));

        group.MapPost("/connect", async (ConnectAccountInput input, IGrainFactory grains, CancellationToken cancellationToken) =>
        {
            // The browser may supply a new value, never a reference into any vault.
            if (input.SecretReference is not null) { return Results.BadRequest(new { error = "Supply a connection value." }); }
            if (string.IsNullOrWhiteSpace(input.ConnectionId) || input.ConnectionId.StartsWith("oauth:", StringComparison.Ordinal)) { return Results.BadRequest(); }
            try
            {
                var request = new ConnectAccount
                {
                    IntegrationId = input.IntegrationId ?? "",
                    ConnectionId = input.ConnectionId,
                    Label = input.Label,
                    Value = input.Value,
                };
                return Results.Ok(Project(await grains.GetGrain<IIntegrationAccounts>(CurrentScope().Id).Connect(request, cancellationToken)));
            }
            catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        });

        group.MapPost("/probe", async (AccountAction input, IGrainFactory grains, IEnumerable<IExternalAccount> external, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(input.ConnectionId)) { return Results.BadRequest(); }
            if (external.FirstOrDefault(account => account.ConnectionId == input.ConnectionId) is { } oauth)
            { return await oauth.ReadAsync(grains, cancellationToken) is { } row ? Results.Ok(row) : Results.NotFound(); }
            try { return Results.Ok(Project(await Registry(grains).Probe(input.ConnectionId, cancellationToken))); }
            catch (AccountNotConfiguredException) { return Results.NotFound(); }
        });

        group.MapPost("/disconnect", async (AccountAction input, IGrainFactory grains, IEnumerable<IExternalAccount> external, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(input.ConnectionId)) { return Results.BadRequest(); }
            if (external.FirstOrDefault(account => account.ConnectionId == input.ConnectionId) is { } oauth)
            {
                await oauth.DisconnectAsync(grains, cancellationToken);
                return Results.Ok();
            }

            await Registry(grains).Disconnect(input.ConnectionId, cancellationToken);
            return Results.Ok();
        });
    }

    private static BrainScope CurrentScope()
    {
        var caller = CallerContextStamper.Require();
        return BrainScope.Create(caller.AccountId, caller.BrainId);
    }

    private static IIntegrationAccounts Registry(IGrainFactory grains) => grains.GetGrain<IIntegrationAccounts>(CurrentScope().Id);

    private static AccountRow Project(IntegrationAccount account)
        => new(account.Id, account.IntegrationId, Status(account.Status), account.LastProbedAt);
}
