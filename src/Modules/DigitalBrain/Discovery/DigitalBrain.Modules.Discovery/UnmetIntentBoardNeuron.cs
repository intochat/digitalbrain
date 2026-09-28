using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Discovery;

[GenerateSerializer, Alias("discovery.board-state")]
internal sealed record UnmetIntentState
{
    [Id(0)] public List<UnmetIntent> Items { get; init; } = [];
}

// One board per silo keyed by a fixed id; misses are grouped by workspace and text.
[GrainType("unmet-intents")]
internal sealed class UnmetIntentBoardNeuron(
    [PersistentState("board", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<UnmetIntentState> store)
    : Neuron, IUnmetIntentBoard
{
    private const int MaxItems = 200;

    public async Task Record(string workspaceId, string text, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        store.State ??= new UnmetIntentState();
        var normalized = text.Trim();
        var existing = store.State.Items.Find(item =>
            string.Equals(item.WorkspaceId, workspaceId, StringComparison.Ordinal)
            && string.Equals(item.Text, normalized, StringComparison.OrdinalIgnoreCase));

        List<UnmetIntent> items;
        if (existing is null)
        {
            items = [.. store.State.Items, new UnmetIntent
            {
                Id = Guid.NewGuid().ToString("N"),
                WorkspaceId = workspaceId,
                Text = normalized,
                Count = 1,
                LastSeenAt = at,
            }];
            if (items.Count > MaxItems)
            {
                items = [.. items.Skip(items.Count - MaxItems)];
            }
        }
        else
        {
            items = [.. store.State.Items.Select(item => item.Id == existing.Id
                ? item with { Count = item.Count + 1, LastSeenAt = at }
                : item)];
        }

        store.State = new UnmetIntentState { Items = items };
        await store.WriteStateAsync().ConfigureAwait(true);
    }

    public Task<IReadOnlyList<UnmetIntent>> Read()
        => Task.FromResult<IReadOnlyList<UnmetIntent>>(store.State?.Items ?? []);
}
