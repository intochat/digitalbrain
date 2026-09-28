using DigitalBrain.Compute;
using DigitalBrain.Contracts;

namespace DigitalBrain.AI.Metering;

// Converts the provider's reported token classes into one idempotent meter event per
// class. A null count means the provider did not report that class, so no event is
// emitted — never a zero-quantity event.
internal static class IntentMeterEvents
{
    public static IEnumerable<MeterEvent> FromUsage(
        string intentId, string? workspaceId, IReadOnlyList<IIntentUsageEntry> usage)
    {
        foreach (var entry in usage.OfType<TokenUsageEntry>())
        {
            var prefix = entry.Meter == MeterKind.Chat ? "llm" : "embedding";
            var source = entry.Meter == MeterKind.Chat ? MeterSource.ChatClient : MeterSource.EmbeddingGenerator;
            foreach (var (step, quantity) in Classes(entry))
            {
                yield return new MeterEvent
                {
                    IntentId = intentId,
                    MeterId = $"{prefix}.{entry.Model}.{step}",
                    Step = step,
                    WorkspaceId = workspaceId ?? string.Empty,
                    Quantity = quantity,
                    Unit = "tokens",
                    Source = source,
                    CostBasis = entry.Provider,
                    OccurredAt = entry.At,
                };
            }
        }
    }

    public static IEnumerable<MeterEvent> FromUsage(string intentId, string? workspaceId, TokenUsageEntry entry)
        => FromUsage(intentId, workspaceId, [entry]);

    private static IEnumerable<(string Step, decimal Quantity)> Classes(TokenUsageEntry entry)
    {
        if (entry.InputTokens is { } input) { yield return ("input_tokens", input); }
        if (entry.CachedInputTokens is { } cached) { yield return ("cached_input_tokens", cached); }
        if (entry.ReasoningTokens is { } reasoning) { yield return ("reasoning_tokens", reasoning); }
        if (entry.OutputTokens is { } output) { yield return ("output_tokens", output); }
        if (entry.TotalTokens is { } total) { yield return ("total_tokens", total); }
    }
}
