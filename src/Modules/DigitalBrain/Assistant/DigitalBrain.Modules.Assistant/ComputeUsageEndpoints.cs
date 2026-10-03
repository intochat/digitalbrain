using System.Text.Json;
using DigitalBrain.AI.Metering;
using DigitalBrain.Compute;
using DigitalBrain.Compute.Usage;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel.Enforcement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Assistant;

public static class ComputeUsageEndpoints
{
    public static string IntentId(string scope, string thread, string run) => AssistantUsage.IntentId(scope, thread, run);

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var compute = BrainRoutes.Group(endpoints, "/compute");
        compute.MapGet("/usage", async (int? limit, string? cursor, IDigitalBrain brain, CancellationToken ct) =>
        {
            var accountId = CallerContextStamper.Require().AccountId;
            var scope = BrainScope.CurrentId();
            UsagePage page;
            try { page = await brain.Get<IComputeUsage>(accountId).Read(scope, limit ?? 20, cursor, ct); }
            catch (ArgumentException) { return Results.BadRequest(new { error = "Invalid usage limit or cursor." }); }
            var ledger = brain.Get<IAllowanceLedger>(accountId);
            var items = new List<ComputeUsageItem>();
            foreach (var row in page.Items)
            {
                var item = JsonSerializer.Deserialize<ComputeUsageItem>(row.Payload)!;
                items.Add(item with
                {
                    OccurredAt = row.OccurredAt,
                    ChargedCompute = await brain.Get<IWallet>(accountId).ReadChargedAsync(item.Id, ct),
                    SettledCompute = await ledger.ReadSettledAsync(scope, item.Id, ct),
                    ReservedCompute = await ledger.ReadReservedAsync(scope, item.Id, ct),
                });
            }
            return Results.Ok(new { items, page.NextCursor });
        });
    }
}
