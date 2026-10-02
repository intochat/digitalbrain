using DigitalBrain.AI.Metering;
using DigitalBrain.Compute;
using DigitalBrain.Contracts;

namespace DigitalBrain.Assistant;

public static class AgentReceipts
{
    public static AgentReceipt Create(IPriceBook priceBook, IntentContext intent, IntentActivity activity, AgentRunOutcome outcome, TokenUsageEntry[]? cumulativeUsage = null)
    {
        var (modelCalls, compute) = ShadowPrice(cumulativeUsage ?? intent.Usage.OfType<TokenUsageEntry>(), priceBook);
        return new AgentReceipt(outcome, Summarize(activity), activity.Calls, activity.Touched, modelCalls, compute);
    }

    // Unknown meters and local models price at zero, so shadow never overcharges.
    public static (int ModelCalls, decimal Compute) ShadowPrice(IEnumerable<TokenUsageEntry> entries, IPriceBook priceBook)
    {
        var modelCalls = 0;
        var compute = 0m;
        foreach (var entry in entries)
        {
            modelCalls++;
            if (entry.Meter == MeterKind.Chat)
            {
                compute += Price(priceBook, $"chat:{entry.Model}:input", entry.InputTokens);
                compute += Price(priceBook, $"chat:{entry.Model}:cached", entry.CachedInputTokens);
                compute += Price(priceBook, $"chat:{entry.Model}:reasoning", entry.ReasoningTokens);
                compute += Price(priceBook, $"chat:{entry.Model}:output", entry.OutputTokens);
            }
            else
            {
                compute += Price(priceBook, $"embedding:{entry.Model}:input", entry.InputTokens);
            }
        }
        return (modelCalls, compute);
    }

    private static decimal Price(IPriceBook priceBook, string meterId, long? tokens)
        => tokens is { } count && count > 0 ? priceBook.PriceInCompute(meterId, count) : 0m;

    private static string Summarize(IntentActivity activity)
    {
        var rows = activity.Touched.Sum(touched => touched.RowsRead);
        var calls = activity.Calls.Count;
        return rows > 0
            ? $"{calls} tool calls · {rows} rows read"
            : $"{calls} tool calls";
    }
}
