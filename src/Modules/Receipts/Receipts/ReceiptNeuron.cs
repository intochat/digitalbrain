using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Receipts.Signals;
using Orleans.Runtime;

namespace DigitalBrain.Receipts;

[GenerateSerializer, Alias("receipts.storage")]
internal sealed record ReceiptStorage
{
    [Id(0)] public Receipt? Receipt { get; init; }
}

// One durable row per intent, keyed by intent id and stored outside the workspace blob.
[GrainType("receipt")]
internal sealed class ReceiptNeuron(
    [PersistentState("receipt", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ReceiptStorage> store)
    : Neuron, IReceipt
{
    public async Task Write(ReceiptDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        store.State ??= new();
        if (store.State.Receipt is not null) { return; }
        var receipt = new Receipt
        {
            IntentId = this.GetPrimaryKeyString(),
            Content = draft,
            WrittenAt = DateTimeOffset.UtcNow,
        };
        store.State = new ReceiptStorage { Receipt = receipt };
        await store.WriteStateAsync();
        await PublishAsync(new IntentRecorded(
            receipt.IntentId, draft.Outcome, draft.ModelCalls, draft.ActualCompute, draft.Touched.Count, receipt.WrittenAt));
    }

    public Task<Receipt?> Read()
    {
        store.State ??= new();
        return Task.FromResult(store.State.Receipt);
    }

    public async Task MarkKept()
    {
        store.State ??= new();
        if (store.State.Receipt is not { Kept: false } receipt) { return; }
        store.State = new ReceiptStorage { Receipt = receipt with { Kept = true } };
        await store.WriteStateAsync();
    }
}
