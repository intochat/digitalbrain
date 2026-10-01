using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.AI.Metering;
using DigitalBrain.Compute;

namespace DigitalBrain.Assistant;

public sealed record ComputeUsageItem(string Id, DateTimeOffset OccurredAt, string Title, string Outcome,
    decimal? PreviewCompute, decimal? ChargedCompute, decimal? ReservedCompute, string PriceBookVersion,
    TokenUsageEntry[] ModelUsage, IReadOnlyList<AgentCall> Calls, IReadOnlyList<AgentTouchedData> Touched, decimal? SettledCompute = null);

public static class AssistantUsage
{
    public static string IntentId(string scope, string thread, string run) => "agent-" + Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { scope, thread, run }))));

    public static ComputeUsageItem Create(string id, AgentReceipt receipt, IPriceBook priceBook, TokenUsageEntry[] modelUsage)
        => new(id, DateTimeOffset.UtcNow, receipt.Calls.Count == 0 ? "Assistant response" : "Assistant · " + receipt.Summary,
            receipt.Outcome.ToString(), modelUsage.Any(entry => !entry.UsageReported || entry.InputTokens is null
                || (entry.Meter == MeterKind.Chat && (entry.OutputTokens is null || entry.CachedInputTokens is null || entry.ReasoningTokens is null)))
                || modelUsage.Any(entry => (entry.TotalTokens > 0 || entry.InputTokens > 0 || entry.OutputTokens > 0
                    || entry.CachedInputTokens > 0 || entry.ReasoningTokens > 0)
                    && AgentReceipts.ShadowPrice([entry], priceBook).Compute == 0)
                ? null : receipt.Compute, null, null,
            priceBook.Version, modelUsage, receipt.Calls, receipt.Touched);

}
