using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>
/// A runtime service that takes delivered signals itself instead of through <c>[On]</c> methods (04-13): the session neuron queues
/// them into batches, the brain listener neuron hands them to silo-local handlers. Module neurons never implement it.
/// </summary>
internal interface ISignalSink
{
    /// <summary>Takes one signal in order. Throwing leaves the cursor before it; the next reference retries.</summary>
    Task Deliver(JournaledSignal item);
}

/// <summary>
/// The brain-wide synapse (04-13, R4-03): one engine entry keyed by the brain (<see cref="BrainWide"/>) with one brain-position
/// cursor, subscribed to the namespace stream of every alias it is asked for, paging <see cref="ISignalHistory.Read(long, int, CancellationToken)"/>
/// for the whole brain and seeded at the brain's last position when created, so no older signal is replayed.
/// </summary>
internal sealed partial class SynapseEngine
{
    /// <summary>The source written for the brain-wide entry.</summary>
    internal const string BrainWide = "*";

    private SynapseSource? BrainEntry => _state.Sources.Find(item => item.Source == BrainWide);

    /// <summary>Ensures the brain-wide synapse holds <paramref name="aliases"/>; creates it at the brain's last position.</summary>
    /// <exception cref="InvalidOperationException">The subscriber is stateless, or the cluster store is not BrainLog.</exception>
    /// <exception cref="ArgumentException">No alias, or an alias that is not registered.</exception>
    public async Task EnsureBrain(string[] aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        if (Stateless) { throw new InvalidOperationException($"{_registration.Class.FullName} is stateless: a brain-wide synapse is durable."); }
        aliases = CheckAliases(aliases);
        if (Store is null) { throw new InvalidOperationException($"{_id} holds synapses, which need a cluster store (BrainStorageRegistration)."); }
        if (!_loaded) { await Load().ConfigureAwait(true); }
        var entry = BrainEntry;
        var created = entry is null;
        if (entry is null)
        {
            var last = (await History.Read(0, 1, CancellationToken.None).ConfigureAwait(true)).Last;
            _counters.Read();
            entry = new SynapseSource { Source = BrainWide, Cursor = last };
            _state.Sources.Add(entry);
        }
        var added = aliases.Where(alias => !entry.Aliases.Contains(alias, StringComparer.Ordinal)).ToArray();
        entry.Aliases.AddRange(added);
        if (created || added.Length > 0) { await Persist().ConfigureAwait(true); }
        foreach (var ns in aliases.Select(StreamNames.SignalNamespace).Distinct(StringComparer.Ordinal))
        {
            await Subscribe(StreamNames.ForNamespace(_id.Brain.Value, ns)).ConfigureAwait(true);
        }
    }

    /// <summary>Removes the brain-wide synapse and the subscriptions only it needed.</summary>
    public async Task RemoveBrain()
    {
        if (Stateless || Store is null) { return; }
        if (!_loaded) { await Load().ConfigureAwait(true); }
        await RemoveSource(BrainWide).ConfigureAwait(true);
    }

    /// <summary>Removes every synapse at once: one write, one unsubscribe per namespace, one <c>Forget</c> (session end and SessionTtl).</summary>
    public async Task RemoveAll()
    {
        if (Stateless || Store is null) { return; }
        if (!_loaded) { await Load().ConfigureAwait(true); }
        if (_state.Sources.Count == 0) { return; }
        var namespaces = _state.Sources.SelectMany(item => item.Aliases).Select(StreamNames.SignalNamespace).Distinct(StringComparer.Ordinal).ToArray();
        _state.Sources.Clear();
        await Persist().ConfigureAwait(true);
        await _grains.GetGrain<ISynapseCursorRegistry>(_id.Brain.Value).Forget(_id.ToString()).ConfigureAwait(true);
        foreach (var ns in namespaces) { await Unsubscribe(StreamNames.ForNamespace(_id.Brain.Value, ns)).ConfigureAwait(true); }
    }

    /// <summary>Whether the brain-wide synapse exists.</summary>
    internal bool HasBrainSynapse => BrainEntry is not null;

    private string[] CheckAliases(string[] aliases)
    {
        if (aliases.Length == 0) { throw new ArgumentException("A synapse names at least one signal alias.", nameof(aliases)); }
        var map = _services.GetRequiredService<JournalPayloads>().Types;
        foreach (var alias in aliases)
        {
            if (alias is null || !map.TryResolve(alias, out _)) { throw new ArgumentException($"'{alias}' is not a registered [Signal] alias.", nameof(aliases)); }
        }
        return [.. aliases.Distinct(StringComparer.Ordinal)];
    }

    // Pages the whole brain from the brain cursor; Gap when the brain floor passed it; wanted signals in brain position order.
    private async Task PullBrain(SynapseSource entry)
    {
        if (Neuron is not { } neuron) { return; }
        if (!_gates.TryGetValue(BrainWide, out var gate)) { _gates[BrainWide] = gate = new SemaphoreSlim(1, 1); }
        await gate.WaitAsync().ConfigureAwait(true);
        try
        {
            var map = _services.GetRequiredService<JournalPayloads>().Types;
            var wanted = entry.Aliases.Select(alias => map.TryResolve(alias, out var type) ? type : null).OfType<Type>().ToArray();
            var history = History;
            while (_state.Sources.Contains(entry))
            {
                var page = await history.Read(entry.Cursor, SignalPage.MaxLimit, CancellationToken.None).ConfigureAwait(true);
                _counters.Read();
                if (page.Floor > entry.Cursor)
                {
                    var gap = new JournaledSignal(_id, page.Floor, new Gap(_id, entry.Cursor, page.Floor), DateTimeOffset.UtcNow);
                    if (!await React(neuron, gap, entry).ConfigureAwait(true)) { return; }
                    await Persist().ConfigureAwait(true);
                    continue;
                }
                var due = page.Signals.Where(item => item.Sequence > entry.Cursor).ToList();
                if (due.Count == 0) { return; }
                foreach (var item in due)
                {
                    if (wanted.Any(type => type.IsInstanceOfType(item.Signal)))
                    {
                        if (!await React(neuron, item, entry).ConfigureAwait(true)) { return; }
                    }
                    else { entry.Cursor = item.Sequence; }
                }
                await Persist().ConfigureAwait(true);
            }
        }
        finally { gate.Release(); }
    }
}
