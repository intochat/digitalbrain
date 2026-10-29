using System.Reflection;
using DigitalBrain.Runtime.Orleans;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Invocation;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Orleans.Streams;
using Orleans.Streams.Core;

namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>
/// The subscribe half of synapses inside one host grain activation (04 D-03, D-04, D-08, D-12):
/// <list type="bullet">
/// <item><see cref="Ensure"/> refuses stateless hosts, self and other-brain sources, runs the source's <c>AdmitSynapse</c> through
/// <see cref="ISynapseHost.Admit"/> under the subscriber's caller identity on every call, persists (source, aliases, cursor) in the
/// subscriber's <see cref="SynapseState"/>, and adds one explicit subscription per (brain, namespace) with
/// <c>IStreamSubscriptionManager.AddSubscription</c>, once.</item>
/// <item><see cref="OnSubscribed"/> resumes the handle with <c>ResumeAsync</c> on every activation; a subscription is never made per
/// activation.</item>
/// <item>A <see cref="SignalReference"/> is only a nudge: at or below the cursor it is dropped without a read; above it the engine
/// pages the brain-checked journal from the cursor, emits <see cref="Gap"/> when the floor passed the cursor, runs each wanted
/// signal's <c>[On]</c> reaction in sequence order; each reaction's save carries the advanced cursor in the same journal entry (R4-09),
/// then the cursor is reported.</item>
/// </list>
/// </summary>
internal sealed partial class SynapseEngine : IAsyncObserver<SignalReference>
{
    private readonly NeuronId _id;
    private readonly GrainId _grainId;
    private readonly NeuronRegistration _registration;
    private readonly IServiceProvider _services;
    private readonly IGrainFactory _grains;
    private readonly NeuronTypeRegistry _types;
    private readonly Func<Task> _drain;
    private readonly SynapseCounters _counters;
    private readonly ILogger _log;
    private readonly Dictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    private SynapseState _state = new();
    private string? _etag;
    private bool _loaded;

    public SynapseEngine(NeuronId id, GrainId grainId, NeuronRegistration registration, IServiceProvider services, IGrainFactory grains,
        NeuronTypeRegistry types, Func<Task> drain)
    {
        _id = id;
        _grainId = grainId;
        _registration = registration;
        _services = services;
        _grains = grains;
        _types = types;
        _drain = drain;
        _counters = services.GetService<SynapseCounters>() ?? new SynapseCounters();
        _log = services.GetService<ILoggerFactory>()?.CreateLogger<SynapseEngine>() ?? NullLogger<SynapseEngine>.Instance;
    }

    /// <summary>The neuron the reactions run on; set once it is attached and activated.</summary>
    public Neuron? Neuron { get; set; }

    /// <summary>The sources and cursors this subscriber holds.</summary>
    internal IReadOnlyList<SynapseSource> Sources => _state.Sources;

    private IGrainStorage? Store => _services.GetKeyedService<IGrainStorage>(IntoChatOSNames.ClusterGrainStorage) is { } store and not UnregisteredClusterStorage ? store : null;

    private bool Stateless => _registration.Policy == HostPolicy.Stateless;

    private ISignalHistory History => (_services.GetService<BrainSignalHistories>()
        ?? throw new InvalidOperationException("Synapses need BrainLog as the cluster store (BrainStorageRegistration).")).For(_id.Brain);

    /// <summary>Loads the subscriber's synapses; stateless hosts and silos without a cluster store hold none.</summary>
    public async Task Load()
    {
        _loaded = true;
        if (Stateless || Store is not { } store) { return; }
        var state = new GrainState<SynapseState>();
        await store.ReadStateAsync(SynapseState.Name, _grainId, state).ConfigureAwait(true);
        _etag = state.ETag;
        var loaded = state.RecordExists && state.State is not null ? state.State : new SynapseState();
        // Only sources of the subscriber's own brain are honoured, whatever the stored record says (T-04-37).
        loaded.Sources.RemoveAll(entry => entry.Source != BrainWide && (!NeuronId.TryParse(entry.Source, out var source) || source.Brain != _id.Brain || source == _id));
        _state = loaded;
    }

    /// <summary>Resumes the explicit subscription handed to this activation; called from <c>IStreamSubscriptionObserver.OnSubscribed</c>.</summary>
    public async Task OnSubscribed(IStreamSubscriptionHandleFactory handleFactory)
    {
        ArgumentNullException.ThrowIfNull(handleFactory);
        var handle = handleFactory.Create<SignalReference>();
        _ = await handle.ResumeAsync(this).ConfigureAwait(true);
    }

    /// <summary>Ensures signals of <paramref name="signals"/> from <paramref name="from"/> reach this neuron.</summary>
    /// <exception cref="InvalidOperationException">The subscriber is a <c>[StatelessNeuron]</c>, or the cluster store is not BrainLog.</exception>
    /// <exception cref="ArgumentException">Self-synapse, no signal, or a type without a registered <c>[Signal]</c> alias.</exception>
    /// <exception cref="UnauthorizedAccessException">The source is in another brain or refuses the subscriber.</exception>
    public Task Ensure(NeuronId from, IReadOnlyList<Type> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        if (signals.Count == 0) { throw new ArgumentException("A synapse names at least one signal type.", nameof(signals)); }
        var map = _services.GetRequiredService<JournalPayloads>().Types;
        var aliases = signals.Select(signal => signal is not null && typeof(Signal).IsAssignableFrom(signal) && map.TryAliasOf(signal, out var alias)
            ? alias
            : throw new ArgumentException($"{signal?.Name} is not a registered [Signal] type.", nameof(signals))).Distinct(StringComparer.Ordinal).ToArray();
        return EnsureAliases(from, aliases);
    }

    /// <summary>
    /// Ensures signals of the <c>[Signal]</c> <paramref name="aliases"/> from <paramref name="from"/> reach this neuron: the alias form
    /// runtime services use (the session neuron, 04-13); module neurons go through <see cref="Ensure"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The subscriber is a <c>[StatelessNeuron]</c>, or the cluster store is not BrainLog.</exception>
    /// <exception cref="ArgumentException">Self-synapse, no alias, or an alias that is not registered.</exception>
    /// <exception cref="UnauthorizedAccessException">The source is in another brain or refuses the subscriber.</exception>
    public async Task EnsureAliases(NeuronId from, string[] aliases)
    {
        aliases = ValidateAliases(from, aliases);
        // Admission on every Ensure, under the subscriber's identity; a refusal throws before anything is stored (T-04-36).
        await AdmitAliases(from, aliases).ConfigureAwait(true);
        await CommitAliases(from, aliases).ConfigureAwait(true);
    }

    // The session's interleaved preparation may validate and ask admission, but never load or mutate engine state.
    internal string[] ValidateAliases(NeuronId from, string[] aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        if (Stateless)
        {
            throw new InvalidOperationException($"{_registration.Class.FullName} is a [StatelessNeuron] class: stateless activations own no durable synapse.");
        }
        if (from == _id) { throw new ArgumentException("A neuron does not subscribe to itself.", nameof(from)); }
        aliases = CheckAliases(aliases);
        if (from.Brain != _id.Brain) { throw new UnauthorizedAccessException($"Neuron {_id} may not subscribe to {from} of another brain."); }
        if (Store is null) { throw new InvalidOperationException($"{_id} holds synapses, which need a cluster store (BrainStorageRegistration)."); }
        return aliases;
    }

    internal Task AdmitAliases(NeuronId from, string[] aliases) => Admit(from, aliases);

    // Only EnsureAliases after admission or the session's validated one-use ticket reaches this ordinary mutation.
    internal async Task CommitAliases(NeuronId from, string[] aliases)
    {
        if (!_loaded) { await Load().ConfigureAwait(true); }
        var source = from.ToString();
        var entry = _state.Sources.Find(item => item.Source == source);
        var created = entry is null;
        if (entry is null)
        {
            var last = (await History.Read(from, 0, 1, CancellationToken.None).ConfigureAwait(true)).Last;
            _counters.Read();
            entry = new SynapseSource { Source = source, Cursor = last };
            _state.Sources.Add(entry);
        }
        var added = aliases.Where(alias => !entry.Aliases.Contains(alias, StringComparer.Ordinal)).ToArray();
        entry.Aliases.AddRange(added);
        if (created || added.Length > 0) { await Persist().ConfigureAwait(true); }
        foreach (var alias in aliases.Select(StreamNames.SignalNamespace).Distinct(StringComparer.Ordinal))
        {
            await Subscribe(StreamNames.ForNamespace(_id.Brain.Value, alias)).ConfigureAwait(true);
        }
        await Report(entry).ConfigureAwait(true);
        if (created) { await Pull(entry, null).ConfigureAwait(true); }
    }

    /// <summary>Removes every synapse from <paramref name="from"/>; a namespace no other source needs loses its subscription.</summary>
    public async Task Remove(NeuronId from)
    {
        if (Stateless || Store is null) { return; }
        if (!_loaded) { await Load().ConfigureAwait(true); }
        await RemoveSource(from.ToString()).ConfigureAwait(true);
    }

    private async Task RemoveSource(string source)
    {
        var entry = _state.Sources.Find(item => item.Source == source);
        if (entry is null) { return; }
        _state.Sources.Remove(entry);
        await Persist().ConfigureAwait(true);
        var kept = _state.Sources.SelectMany(item => item.Aliases).Select(StreamNames.SignalNamespace).ToHashSet(StringComparer.Ordinal);
        foreach (var ns in entry.Aliases.Select(StreamNames.SignalNamespace).Distinct(StringComparer.Ordinal).Where(ns => !kept.Contains(ns)))
        {
            await Unsubscribe(StreamNames.ForNamespace(_id.Brain.Value, ns)).ConfigureAwait(true);
        }
        var registry = _grains.GetGrain<ISynapseCursorRegistry>(_id.Brain.Value);
        await registry.Forget(_id.ToString()).ConfigureAwait(true);
        foreach (var other in _state.Sources) { await Report(other).ConfigureAwait(true); }
    }

    /// <summary>Catches up every source from its cursor: on activation and when reconcile asks.</summary>
    public async Task CatchUp()
    {
        if (!_loaded) { await Load().ConfigureAwait(true); }
        foreach (var entry in _state.Sources.ToArray())
        {
            try { await Pull(entry, null).ConfigureAwait(true); }
            catch (Exception error) when (error is not OutOfMemoryException) { LogCatchUpFailed(_log, error, _id.ToString(), entry.Source); }
        }
    }

    /// <inheritdoc />
    public async Task OnNextAsync(SignalReference item, StreamSequenceToken? token = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!_loaded) { await Load().ConfigureAwait(true); }
        var source = item.Publisher.ToString();
        var entry = _state.Sources.Find(held => held.Source == source);
        if (entry is null)
        {
            // A brain-wide synapse takes every reference of its namespaces; it dedupes by brain position, not by the reference.
            if (BrainEntry is not { } brain || brain.Aliases.Count == 0) { return; } // references of sources this neuron does not hold are ignored
            try { await PullBrain(brain).ConfigureAwait(true); }
            catch (Exception error) when (error is not OutOfMemoryException) { LogCatchUpFailed(_log, error, _id.ToString(), BrainWide); }
            return;
        }
        try { await Pull(entry, item.Sequence).ConfigureAwait(true); }
        catch (Exception error) when (error is not OutOfMemoryException) { LogCatchUpFailed(_log, error, _id.ToString(), source); }
    }

    /// <inheritdoc />
    public Task OnCompletedAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public Task OnErrorAsync(Exception ex)
    {
        LogStreamError(_log, ex, _id.ToString());
        return Task.CompletedTask;
    }

    // Pages the source's journal from the cursor. A reference (through) at or below the cursor is a duplicate and costs no read.
    private async Task Pull(SynapseSource entry, long? reference)
    {
        if (entry.Source == BrainWide)
        {
            await PullBrain(entry).ConfigureAwait(true);
            return;
        }
        if (Neuron is not { } neuron) { return; }
        if (!_gates.TryGetValue(entry.Source, out var gate)) { _gates[entry.Source] = gate = new SemaphoreSlim(1, 1); }
        await gate.WaitAsync().ConfigureAwait(true);
        var start = entry.Cursor;
        try
        {
            if (reference is { } sequence && sequence <= entry.Cursor)
            {
                _counters.Duplicate();
                return;
            }
            var source = NeuronId.Parse(entry.Source);
            var map = _services.GetRequiredService<JournalPayloads>().Types;
            var wanted = entry.Aliases.Select(alias => map.TryResolve(alias, out var type) ? type : null).OfType<Type>().ToArray();
            var history = History;
            while (_state.Sources.Contains(entry))
            {
                // Signals of unknown type (adapter-journaled os signals, 04-17) come back as UnknownSignal: unwanted, so the cursor passes them.
                var page = await (history is BrainSignalHistory journal
                    ? journal.Read(source, entry.Cursor, SignalPage.MaxLimit, keepUnknown: true, CancellationToken.None)
                    : history.Read(source, entry.Cursor, SignalPage.MaxLimit, CancellationToken.None)).ConfigureAwait(true);
                _counters.Read();
                if (page.Floor > entry.Cursor)
                {
                    if (!await React(neuron, new JournaledSignal(source, page.Floor, new Gap(source, entry.Cursor, page.Floor), DateTimeOffset.UtcNow), entry).ConfigureAwait(true)) { return; }
                    continue;
                }
                var due = page.Signals.Where(item => item.Sequence > entry.Cursor).ToList();
                if (due.Count == 0) { return; }
                var skipped = false;
                foreach (var item in due)
                {
                    if (wanted.Any(type => type.IsInstanceOfType(item.Signal)))
                    {
                        // Signals skipped before a reaction ride in its entry: the cursor it writes is past them.
                        skipped = false;
                        if (!await React(neuron, item, entry).ConfigureAwait(true)) { return; }
                    }
                    else
                    {
                        entry.Cursor = item.Sequence;
                        skipped = true;
                    }
                }
                if (skipped) { await Persist().ConfigureAwait(true); }
                await Report(entry).ConfigureAwait(true);
            }
        }
        finally
        {
            gate.Release();
            if (entry.Cursor != start) { await Report(entry).ConfigureAwait(true); }
        }
    }

    // One reaction (R4-09): the cursor moves to the signal's sequence before it runs and every save or publish of the reaction writes
    // it in the same journal entry; a reaction that wrote nothing gets one cursor-only entry. A failure before any write puts the cursor
    // back and logs it; once an entry carried the cursor, the reaction counts as done, so a stop after its save never runs it twice.
    // A runtime service that is an ISignalSink takes the journaled signal itself; a module neuron runs its [On] method.
    private async Task<bool> React(Neuron neuron, JournaledSignal item, SynapseSource entry)
    {
        var before = entry.Cursor;
        entry.Cursor = item.Sequence;
        using var scope = ReactionCursor.Open(_grainId, () => JournalPayloads.EncodeState(_state), _etag);
        try
        {
            if (neuron is ISignalSink sink) { await sink.Deliver(item).ConfigureAwait(true); }
            else { await ReactionDispatcher.React(neuron, item.Signal, CancellationToken.None).ConfigureAwait(true); }
            NoteDelivery(entry, item);
            await _drain().ConfigureAwait(true);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _counters.Failure();
            LogReactionFailed(_log, error, _id.ToString(), entry.Source, item.Sequence);
            if (scope.Cursor.Written)
            {
                _etag = scope.Cursor.ETag;
                return false;
            }
            entry.Cursor = before;
            return false;
        }
        if (scope.Cursor.Written) { _etag = scope.Cursor.ETag; }
        else { await Persist().ConfigureAwait(true); }
        return true;
    }

    private async Task Admit(NeuronId from, string[] aliases)
    {
        if (!_types.TryGet(from.Type, out var sourceClass)) { throw new BindingException($"No neuron class is hosted for type '{from.Type}'."); }
        var host = _grains.GetGrain<ISynapseHost>(GrainId.Create(NeuronTypeRegistry.GrainTypeOf(NeuronTypeRegistry.PolicyOf(sourceClass)), from.ToString()));
        var previous = RequestContext.Get(HostNeuronContext.CallerKey);
        RequestContext.Set(HostNeuronContext.CallerKey, _id.ToString());
        try { await host.Admit(_id, aliases).ConfigureAwait(true); }
        finally
        {
            if (previous is null) { RequestContext.Remove(HostNeuronContext.CallerKey); }
            else { RequestContext.Set(HostNeuronContext.CallerKey, previous); }
        }
    }

    private async Task Persist()
    {
        if (Store is not { } store) { return; }
        // A reaction's state saves also write this record atomically. Ensure/Remove may run between those saves,
        // so both write paths must advance the same ETag before the reaction has finished.
        var reaction = ReactionCursor.For(_grainId);
        var state = new GrainState<SynapseState>(_state) { ETag = reaction?.ETag ?? _etag };
        await store.WriteStateAsync(SynapseState.Name, _grainId, state).ConfigureAwait(true);
        _etag = state.ETag;
        if (reaction is not null && _etag is not null) { reaction.Committed(_etag); }
    }

    private void NoteDelivery(SynapseSource entry, JournaledSignal item)
    {
        if (entry.Source == BrainWide || item.Signal is Gap || _id.Brain.Value is not { } brain) { return; }
        var alias = item.Signal.GetType().GetCustomAttribute<SignalAttribute>(inherit: false)?.Alias ?? item.Signal.GetType().Name;
        _services.GetService<DeliveryHub>()?.Publish(brain, new SignalDelivery(entry.Source, _id.ToString(), alias, item.Sequence));
    }

    private Task Report(SynapseSource entry)
        => entry.Source == BrainWide || !_state.Sources.Contains(entry) ? Task.CompletedTask : _grains.GetGrain<ISynapseCursorRegistry>(_id.Brain.Value).ReportCursor(_id.ToString(), NeuronId.Parse(entry.Source), entry.Cursor);

    private static IStreamSubscriptionManager Manager(IServiceProvider services)
        => services.GetRequiredService<IStreamSubscriptionManagerAdmin>().GetStreamSubscriptionManager(StreamSubscriptionManagerType.ExplicitSubscribeOnly);

    // AddSubscription once per (brain, namespace): an existing subscription of this grain is kept, never duplicated.
    private async Task Subscribe(StreamId stream)
    {
        var manager = Manager(_services);
        var existing = await manager.GetSubscriptions(IntoChatOSNames.SignalStreams, stream).ConfigureAwait(true);
        if (existing.Any(subscription => subscription.GrainId == _grainId)) { return; }
        var self = (GrainReference)(object)_grains.GetGrain<ISynapseHost>(_grainId);
        _ = await manager.AddSubscription(IntoChatOSNames.SignalStreams, stream, self).ConfigureAwait(true);
    }

    private async Task Unsubscribe(StreamId stream)
    {
        var manager = Manager(_services);
        foreach (var subscription in (await manager.GetSubscriptions(IntoChatOSNames.SignalStreams, stream).ConfigureAwait(true)).Where(s => s.GrainId == _grainId))
        {
            await manager.RemoveSubscription(IntoChatOSNames.SignalStreams, stream, subscription.SubscriptionId).ConfigureAwait(true);
        }
    }

    [LoggerMessage(EventId = 4120, EventName = "SynapseReactionFailed", Level = LogLevel.Warning,
        Message = "The [On] reaction of {Subscriber} to {Source} sequence {Sequence} failed; unless a save of the reaction already carried the cursor, the cursor stays before it and the next reference retries.")]
    private static partial void LogReactionFailed(ILogger logger, Exception error, string subscriber, string source, long sequence);

    [LoggerMessage(EventId = 4121, EventName = "SynapseCatchUpFailed", Level = LogLevel.Warning, Message = "Catch-up of {Subscriber} on {Source} failed.")]
    private static partial void LogCatchUpFailed(ILogger logger, Exception error, string subscriber, string source);

    [LoggerMessage(EventId = 4122, EventName = "SynapseStreamError", Level = LogLevel.Warning, Message = "The signal stream of {Subscriber} reported an error.")]
    private static partial void LogStreamError(ILogger logger, Exception error, string subscriber);
}
