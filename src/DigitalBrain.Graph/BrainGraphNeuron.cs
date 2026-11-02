using DigitalBrain.Spi;

namespace DigitalBrain.Graph;

/// <summary>
/// The per-brain graph projection. It rebuilds its snapshot from the sources when asked, or when the in-memory snapshot is older than
/// <see cref="Staleness"/>, and bumps its revision only when the graph changed. It is on no hot path: nothing in a call, a save or a delivery
/// touches it. Analysis runs in its own turn and is cached by revision.
/// </summary>
[Neuron("brain.graph", State = "graph")]
public sealed class BrainGraphNeuron : Neuron<BrainGraphState>, IBrainGraph
{
    /// <summary>How old an in-memory snapshot may be before a read rebuilds it.</summary>
    public static TimeSpan Staleness { get; } = TimeSpan.FromSeconds(30);

    private GraphSnapshot? _snapshot;
    private GraphAnalysis? _analysis;
    private DateTimeOffset _builtAt;

    /// <inheritdoc />
    public async Task<GraphSnapshot> Snapshot(GraphLevel level, string? focus)
    {
        var g = await Current().ConfigureAwait(true);
        return level == GraphLevel.Neurons && focus is null ? g : Grouping.View(g, Analyse(g), level, focus);
    }

    /// <inheritdoc />
    public async Task<GraphAnalysis> Analysis() => Analyse(await Current().ConfigureAwait(true));

    /// <inheritdoc />
    public async Task<string[]> Neighbourhood(string id, int hops, GraphEdgeKind[]? kinds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var g = await Current().ConfigureAwait(true);
        var start = g.IndexOf(id);
        if (start < 0) { return []; }
        return [.. Walks.Hops(g, start, Math.Max(0, hops), kinds).Select(i => g.Ids[i])];
    }

    /// <inheritdoc />
    public async Task<GraphPath[]> Paths(string from, string to, int maxHops)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        var g = await Current().ConfigureAwait(true);
        var a = g.IndexOf(from);
        var b = g.IndexOf(to);
        if (a < 0 || b < 0) { return []; }
        return Walks.Shortest(g, a, b, Math.Max(1, maxHops), null) is { } path ? [path] : [];
    }

    /// <inheritdoc />
    public async Task<long> Rebuild()
    {
        await Build().ConfigureAwait(true);
        return State.Revision;
    }

    private async Task<GraphSnapshot> Current()
    {
        if (_snapshot is null || Context.Time.GetUtcNow() - _builtAt > Staleness) { await Build().ConfigureAwait(true); }
        return _snapshot!;
    }

    private async Task Build()
    {
        var observer = Context.Services.GetService(typeof(IBrainObserver)) as IBrainObserver
            ?? throw new InvalidOperationException("No IBrainObserver is registered: the brain graph reads neuron instances and synapses from it.");
        var sources = Context.Services.GetService(typeof(IEnumerable<IGraphSource>)) as IEnumerable<IGraphSource> ?? [];
        var candidate = await GraphBuilder.Build(Id.Brain, observer, sources, State.Revision + 1, CancellationToken.None).ConfigureAwait(true);
        var hash = Csr.Hash(candidate);
        _builtAt = Context.Time.GetUtcNow();
        if (State.Revision > 0 && hash == State.Hash && _snapshot is not null)
        {
            return;
        }
        if (State.Revision > 0 && hash == State.Hash)
        {
            _snapshot = candidate with { Revision = State.Revision };
            _analysis = null;
            return;
        }
        await Save(new BrainGraphState(candidate.Revision, hash), new GraphChanged(candidate.Revision, candidate.NodeCount, candidate.EdgeCount)).ConfigureAwait(true);
        _snapshot = candidate;
        _analysis = null;
    }

    private GraphAnalysis Analyse(GraphSnapshot g)
    {
        if (_analysis?.Revision == g.Revision) { return _analysis; }
        var pageRank = PageRank.Compute(g);
        var (hub, authority) = Hits.Compute(g);
        _analysis = new GraphAnalysis(g.Revision, Components.Weak(g), Scc.Tarjan(g), Communities.LabelPropagation(g), pageRank, hub, authority, Rules.Run(g, pageRank));
        return _analysis;
    }
}
