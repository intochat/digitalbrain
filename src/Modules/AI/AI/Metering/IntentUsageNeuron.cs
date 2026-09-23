using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.AI.Metering;

[GenerateSerializer, Alias("ai.intent-usage-storage")]
internal sealed record IntentUsageStorage
{
    [Id(0)] public List<TokenUsageEntry> Entries { get; init; } = [];
}

[GrainType("ai-intent-usage")]
internal sealed class IntentUsageNeuron(
    [PersistentState("intent-usage", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<IntentUsageStorage> store)
    : Neuron, IIntentUsage
{
    public async Task RecordAsync(TokenUsageEntry entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entry);
        store.State ??= new();
        store.State.Entries.Add(entry);
        await store.WriteStateAsync();
    }

    public async Task RecordBatchAsync(TokenUsageEntry[] entries, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (entries is null || entries.Length == 0) { return; }
        store.State ??= new();
        store.State.Entries.AddRange(entries);
        await store.WriteStateAsync();
    }

    public Task<IntentTokenUsage> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        store.State ??= new();
        return Task.FromResult(new IntentTokenUsage(this.GetPrimaryKeyString(), [.. store.State.Entries]));
    }
}