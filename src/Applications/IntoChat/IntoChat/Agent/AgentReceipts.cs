using DigitalBrain.AI.Metering;
using DigitalBrain.Compute;
using DigitalBrain.Contracts;
using DigitalBrain.Receipts;

namespace IntoChat.Agent;

// What one intent actually did, collected while the turn runs so the receipt is written once at
// the end from durable facts rather than sampled traces.
internal sealed class IntentActivity
{
    private readonly Dictionary<string, TouchedData> _touched = new(StringComparer.Ordinal);

    public List<AppCall> Calls { get; } = [];
    public IReadOnlyList<TouchedData> Touched => [.. _touched.Values];
    public int Failures { get; private set; }
    public string? FailureExplanation { get; set; }

    public void RecordTool(string name, bool succeeded, string? source, long rowsRead, string? failure = null)
    {
        Calls.Add(new AppCall { AppId = name, Operation = name, Discovered = false, Succeeded = succeeded });
        if (!succeeded)
        {
            Failures++;
            FailureExplanation ??= failure;
            return;
        }
        if (rowsRead <= 0 && string.IsNullOrWhiteSpace(source)) { return; }
        var key = string.IsNullOrWhiteSpace(source) ? name : source;
        _touched[key] = _touched.TryGetValue(key, out var existing)
            ? existing with { RowsRead = existing.RowsRead + rowsRead }
            : new TouchedData { Source = key, SemanticTypeId = "table", ReadOnly = true, RowsRead = rowsRead };
    }
}

internal static class AgentReceipts
{
    public static async Task<ReceiptDraft?> TryWriteAsync(IDigitalBrain brain, IPriceBook priceBook,
        IntentContext intent, string workspaceId, string conversationId, IntentActivity activity,
        ReceiptOutcome outcome, string? failure)
    {
        try
        {
            var (modelCalls, compute) = ShadowPrice(intent, priceBook);
            var draft = new ReceiptDraft
            {
                WorkspaceId = workspaceId,
                ConversationId = conversationId,
                Outcome = outcome,
                Summary = Summarize(activity),
                Calls = activity.Calls,
                Touched = activity.Touched,
                ModelCalls = modelCalls,
                EstimatedCompute = compute,
                ApprovedComputeLimit = 0m,
                ActualCompute = compute,
                ShadowPriced = true,
                FirstTry = activity.Failures == 0,
                Retries = 0,
                Repairs = activity.Failures,
                FailureExplanation = failure,
            };
            await brain.Get<IReceipt>(intent.IntentId).Write(draft);
            return draft;
        }
        catch
        {
            // A receipt observer must never fail or hide the run the user already saw.
            return null;
        }
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
