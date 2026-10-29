using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Synapses;

[GenerateSerializer]
internal sealed class CursorEntry
{
    [Id(0)]
    public string Publisher { get; set; } = string.Empty;

    [Id(1)]
    public string Subscriber { get; set; } = string.Empty;

    [Id(2)]
    public long Cursor { get; set; }

    [Id(3)]
    public DateTimeOffset At { get; set; }
}

[GenerateSerializer]
internal sealed class SynapseCursorRegistryState
{
    [Id(0)]
    public List<CursorEntry> Entries { get; set; } = [];

    /// <summary>True once the brain is in <see cref="ISynapseRegistryIndex"/>.</summary>
    [Id(1)]
    public bool Indexed { get; set; }
}

[DigitalBrain.Runtime.Orleans.Placement.BrainAffinePlacement]
internal sealed class SynapseCursorRegistry(
    [PersistentState("synapse-cursors", IntoChatOSNames.ClusterGrainStorage)] IPersistentState<SynapseCursorRegistryState> state,
    IOptions<RetentionOptions> options,
    TimeProvider time) : Grain, ISynapseCursorRegistry
{
    private string Brain => this.GetPrimaryKeyString();

    public async Task ReportCursor(string subscriber, NeuronId publisher, long cursor)
    {
        ArgumentException.ThrowIfNullOrEmpty(subscriber);
        var name = Own(publisher);
        var entry = state.State.Entries.Find(e => e.Publisher == name && e.Subscriber == subscriber);
        if (entry is null)
        {
            entry = new CursorEntry { Publisher = name, Subscriber = subscriber };
            state.State.Entries.Add(entry);
        }
        entry.Cursor = cursor;
        entry.At = time.GetUtcNow();
        if (!state.State.Indexed)
        {
            await GrainFactory.GetGrain<ISynapseRegistryIndex>(SynapseRegistryIndex.Key).Add(Brain);
            state.State.Indexed = true;
        }
        await state.WriteStateAsync();
    }

    public Task<long?> LowestLive(NeuronId publisher)
    {
        var live = Live(Own(publisher)).ToList();
        return Task.FromResult(live.Count == 0 ? (long?)null : live.Min(e => e.Cursor));
    }

    public async Task Forget(string subscriber)
    {
        if (state.State.Entries.RemoveAll(e => e.Subscriber == subscriber) == 0) { return; }
        if (state.State.Entries.Count == 0 && state.State.Indexed)
        {
            await GrainFactory.GetGrain<ISynapseRegistryIndex>(SynapseRegistryIndex.Key).Remove(Brain);
            state.State.Indexed = false;
        }
        await state.WriteStateAsync();
    }

    public Task<string[]> Behind(NeuronId publisher, long last)
        => Task.FromResult(Live(Own(publisher)).Where(e => e.Cursor < last).Select(e => e.Subscriber).ToArray());

    public Task<NeuronId[]> Publishers()
        => Task.FromResult(state.State.Entries.Select(e => e.Publisher).Distinct(StringComparer.Ordinal).Select(NeuronId.Parse).ToArray());

    private IEnumerable<CursorEntry> Live(string publisher)
    {
        var cutoff = time.GetUtcNow() - options.Value.StaleCursorAfter;
        return state.State.Entries.Where(e => e.Publisher == publisher && e.At >= cutoff);
    }

    /// <summary>A registry only answers for its own brain's publishers (T-04-09).</summary>
    private string Own(NeuronId publisher)
    {
        if (publisher.Brain.Value is null || !string.Equals(publisher.Brain.Value, Brain, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException($"The synapse registry of brain '{Brain}' does not answer for publisher '{publisher}'.");
        }
        return publisher.ToString();
    }
}

/// <summary>Answers <see cref="ISynapseCursorLookup"/> through the brain's registry grain.</summary>
internal sealed class GrainSynapseCursorLookup(IGrainFactory grains) : ISynapseCursorLookup
{
    public async ValueTask<long?> LowestLive(string brainKey, NeuronId publisher)
        => await grains.GetGrain<ISynapseCursorRegistry>(brainKey).LowestLive(publisher).ConfigureAwait(false);
}
