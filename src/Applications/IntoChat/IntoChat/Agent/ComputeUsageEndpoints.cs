using DigitalBrain.Identity.Configuration;
using DigitalBrain.Apps.Assistant;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.AI.Metering;
using DigitalBrain.Compute;
using DigitalBrain.Compute.Usage;
using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using IntoChat.Workspace;
using Microsoft.Extensions.Options;
using DigitalBrain.Identity;

namespace IntoChat.Agent;

internal static class ComputeUsageEndpoints
{
    internal static string IntentId(string scope, string thread, string run) => AssistantUsage.IntentId(scope, thread, run);
    public static void MapComputeUsage(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/compute/summary", async (IDigitalBrain brain, CancellationToken ct) =>
        {
            var ledger = brain.Get<IAllowanceLedger>(CallerContextStamper.Require().AccountId);
            var limits = await ledger.ReadLimitsAsync(ct);
            return Results.Ok(new
            {
                chargedCompute = await brain.Get<IWallet>(CallerContextStamper.Require().AccountId).ReadChargedAsync(cancellationToken: ct),
                settledCompute = await ledger.ReadSettledAsync(cancellationToken: ct),
                reservedCompute = await ledger.ReadReservedAsync(cancellationToken: ct),
                limits.LimitCompute, limits.SpentCompute, limits.HardStopped,
            });
        });
        routes.MapGet("/workspaces/{workspaceId}/compute/usage", async (string workspaceId, int? limit, string? cursor,
            HttpContext http, IDigitalBrain brain, IUsageStore store, IOptions<BasicAuthOptions> auth, CancellationToken ct) =>
        {
            if (!WorkspaceScope.IsValidId(workspaceId)) { return Results.BadRequest(); }
            var caller = CallerContextStamper.Require();
            var scope = WorkspaceScope.Current(auth.Value, workspaceId);
            UsagePage page;
            try { page = await store.ReadAsync(caller.AccountId, scope.Id, limit ?? 20, cursor, ct); }
            catch (ArgumentException) { return Results.BadRequest(new { error = "Invalid usage limit or cursor." }); }
            var ledger = brain.Get<IAllowanceLedger>(caller.AccountId);
            var items = new List<ComputeUsageItem>();
            foreach (var row in page.Items)
            {
                var item = JsonSerializer.Deserialize<ComputeUsageItem>(row.Payload)!;
                items.Add(item with
                {
                    OccurredAt = row.OccurredAt,
                    ChargedCompute = await brain.Get<IWallet>(caller.AccountId).ReadChargedAsync(item.Id, ct),
                    SettledCompute = await ledger.ReadSettledAsync(scope.Id, item.Id, ct),
                    ReservedCompute = await ledger.ReadReservedAsync(scope.Id, item.Id, ct),
                });
            }
            return Results.Ok(new { items, page.NextCursor });
        }).AddEndpointFilter(WorkspaceAccessFilter.EnforceAsync);
    }
}
