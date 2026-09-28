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

internal sealed record ComputeUsageItem(string Id, DateTimeOffset OccurredAt, string Title, string Outcome,
    decimal? PreviewCompute, decimal? ChargedCompute, decimal? ReservedCompute, string PriceBookVersion,
    TokenUsageEntry[] ModelUsage, IReadOnlyList<AgentCall> Calls, IReadOnlyList<AgentTouchedData> Touched, decimal? SettledCompute = null);

internal static class ComputeUsageEndpoints
{
    internal static string IntentId(string scope, string thread, string run) => "agent-" + Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { scope, thread, run }))));

    internal static ComputeUsageItem Create(string id, AgentReceipt receipt, IPriceBook priceBook, TokenUsageEntry[] modelUsage)
        => new(id, DateTimeOffset.UtcNow, receipt.Calls.Count == 0 ? "Assistant response" : "Assistant · " + receipt.Summary,
            receipt.Outcome.ToString(), modelUsage.Any(entry => !entry.UsageReported || entry.InputTokens is null
                || (entry.Meter == MeterKind.Chat && (entry.OutputTokens is null || entry.CachedInputTokens is null || entry.ReasoningTokens is null)))
                || modelUsage.Any(entry => (entry.TotalTokens > 0 || entry.InputTokens > 0 || entry.OutputTokens > 0
                    || entry.CachedInputTokens > 0 || entry.ReasoningTokens > 0)
                    && AgentReceipts.ShadowPrice([entry], priceBook).Compute == 0)
                ? null : receipt.Compute, null, null,
            priceBook.Version, modelUsage, receipt.Calls, receipt.Touched);

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
            if (!AgentEndpoints.ValidId(workspaceId)) { return Results.BadRequest(); }
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
