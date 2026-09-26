using DigitalBrain.AI.Metering;
using DigitalBrain.Compute;
using DigitalBrain.Contracts;

namespace IntoChat.Agent;

internal enum AgentRunOutcome { Succeeded, Failed, Cancelled }

internal sealed record AgentCall(string AppId, string Operation, bool Discovered, bool Succeeded);

internal sealed record AgentTouchedData(string Source, string SemanticTypeId, bool ReadOnly, long RowsRead);

internal sealed record AgentReceipt(
    AgentRunOutcome Outcome,
    string Summary,
    IReadOnlyList<AgentCall> Calls,
    IReadOnlyList<AgentTouchedData> Touched,
    int ModelCalls,
    decimal Compute);

// Collected during a turn for the receipt card sent in the agent stream.
internal sealed class IntentActivity
{
    private readonly Dictionary<string, AgentTouchedData> _touched = new(StringComparer.Ordinal);

    public List<AgentCall> Calls { get; } = [];
    public IReadOnlyList<AgentTouchedData> Touched => [.. _touched.Values];

    public void RecordTool(string name, bool succeeded, string? source, long rowsRead)
    {
        Calls.Add(new AgentCall(name, name, false, succeeded));
        if (!succeeded)
        {
            return;
        }
        if (rowsRead <= 0 && string.IsNullOrWhiteSpace(source)) { return; }
        var key = string.IsNullOrWhiteSpace(source) ? name : source;
        _touched[key] = _touched.TryGetValue(key, out var existing)
            ? existing with { RowsRead = existing.RowsRead + rowsRead }
            : new AgentTouchedData(key, "table", true, rowsRead);
    }
}

internal static class AgentReceipts
{
    public static AgentReceipt Create(IPriceBook priceBook, IntentContext intent, IntentActivity activity, AgentRunOutcome outcome)
    {
        var (modelCalls, compute) = ShadowPrice(intent, priceBook);
        return new AgentReceipt(outcome, Summarize(activity), activity.Calls, activity.Touched, modelCalls, compute);
    }

    // Model calls and shadow Compute come from the intent's durable usage batch, priced by the
    // one price book. Unknown meters and local models price at zero, so shadow never overcharges.
    internal static (int ModelCalls, decimal Compute) ShadowPrice(IntentContext intent, IPriceBook priceBook)
    {
        var modelCalls = 0;
        var compute = 0m;
        foreach (var entry in intent.Usage.OfType<TokenUsageEntry>())
        {
            modelCalls++;
            if (entry.Meter == MeterKind.Chat)
            {
                compute += Price(priceBook, PriceBook.ChatMeterId(entry.Model, "input"), entry.InputTokens);
                compute += Price(priceBook, PriceBook.ChatMeterId(entry.Model, "cached"), entry.CachedInputTokens);
                compute += Price(priceBook, PriceBook.ChatMeterId(entry.Model, "reasoning"), entry.ReasoningTokens);
                compute += Price(priceBook, PriceBook.ChatMeterId(entry.Model, "output"), entry.OutputTokens);
            }
            else
            {
                compute += Price(priceBook, PriceBook.EmbeddingMeterId(entry.Model), entry.InputTokens);
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
