using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using DigitalBrain.Spi;

namespace DigitalBrain.Testing;

// The synapses one activation holds as a subscriber; durable entries live in the TestOS registry.
internal sealed class TestSynapses(TestNeuronHost host) : ISynapses
{
    public Task Ensure<TSignal>(NeuronId from) where TSignal : Signal => Ensure(from, typeof(TSignal));

    public Task Ensure(NeuronId from, params Type[] signals) => host.OS.Synapses.Ensure(host, from, signals);

    public Task Remove(NeuronId from)
    {
        host.OS.Synapses.Remove(host.Id, from);
        return Task.CompletedTask;
    }
}

// Durable synapses of a TestOS: per subscriber, per source, the signal types and the cursor. They survive deactivation.
internal sealed class SynapseRegistry(TestOS os)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<NeuronId, Dictionary<NeuronId, Entry>> _bySubscriber = [];
    private static readonly ConcurrentDictionary<(Type Class, Type Signal), MethodInfo?> Reactions = new();
    private readonly ConcurrentDictionary<NeuronId, SemaphoreSlim> _pumps = new();
    // The reaction running in this flow: its subscriber, synapse and the cursor its first journal write carries (R4-09).
    private static readonly AsyncLocal<Reaction?> Reacting = new();
    private long _duplicates;

    public long DuplicatesDropped => Interlocked.Read(ref _duplicates);

    public async Task Ensure(TestNeuronHost subscriber, NeuronId from, Type[] signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        if (subscriber.Class.IsDefined(typeof(StatelessNeuronAttribute), false))
        {
            throw new InvalidOperationException($"{subscriber.Class.FullName} is a [StatelessNeuron] and holds no synapses.");
        }
        if (from == subscriber.Id) { throw new ArgumentException("A neuron does not subscribe to itself.", nameof(from)); }
        if (signals.Length == 0) { throw new ArgumentException("A synapse names at least one signal type.", nameof(signals)); }
        foreach (var signal in signals)
        {
            if (signal is null || !typeof(Signal).IsAssignableFrom(signal)) { throw new ArgumentException($"{signal} is not a signal type.", nameof(signals)); }
        }
        if (from.Brain != subscriber.Id.Brain) { throw new UnauthorizedAccessException($"Neuron {subscriber.Id} may not subscribe to {from} of another brain."); }

        // Admission is a turn on the source, through the runtime's single entry point.
        IReadOnlyList<Type> named = [.. signals];
        var source = os.Host(from);
        var request = new SynapseRequest(subscriber.Id, named, os.Caller);
        await os.Track(source.Turn(interleave: false, async neuron =>
        {
            await NeuronAttachment.Admit(neuron, request).ConfigureAwait(false);
            return 0;
        })).ConfigureAwait(false);

        lock (_gate)
        {
            if (!_bySubscriber.TryGetValue(subscriber.Id, out var sources)) { _bySubscriber[subscriber.Id] = sources = []; }
            if (!sources.TryGetValue(from, out var entry))
            {
                sources[from] = entry = new Entry(from, subscriber.Id) { Cursor = os.Journal(from.Brain).Last(from), CursorAt = os.Clock.GetUtcNow() };
            }
            entry.Signals.UnionWith(named);
        }
    }

    public void Remove(NeuronId subscriber, NeuronId from)
    {
        lock (_gate)
        {
            if (_bySubscriber.TryGetValue(subscriber, out var sources)) { sources.Remove(from); }
        }
    }

    public ObservedSynapse[] All()
    {
        lock (_gate)
        {
            return [.. _bySubscriber.SelectMany(pair => pair.Value.Values.SelectMany(entry => entry.Signals.Select(signal =>
                new ObservedSynapse(entry.Source.ToString(), pair.Key.ToString(), AliasOf(signal)))))
                .OrderBy(row => row.From, StringComparer.Ordinal).ThenBy(row => row.To, StringComparer.Ordinal).ThenBy(row => row.Signal, StringComparer.Ordinal)];
        }
    }

    private static string AliasOf(Type signal) => signal.GetCustomAttribute<SignalAttribute>(false)?.Alias ?? signal.Name;

    public IReadOnlyList<Synapse> Of(NeuronId subscriber)
    {
        lock (_gate)
        {
            return _bySubscriber.TryGetValue(subscriber, out var sources)
                ? [.. sources.Values.SelectMany(entry => entry.Signals.Select(signal => new Synapse(entry.Source, signal, subscriber))).OrderBy(synapse => synapse.From.ToString(), StringComparer.Ordinal).ThenBy(synapse => synapse.Signal.FullName, StringComparer.Ordinal)]
                : [];
        }
    }

    public long? Cursor(NeuronId subscriber, NeuronId source)
    {
        lock (_gate) { return _bySubscriber.TryGetValue(subscriber, out var sources) && sources.TryGetValue(source, out var entry) ? entry.Cursor : null; }
    }

    // The lowest cursor on the publisher that advanced within StaleCursorAfter; stale cursors stop pinning the floor.
    public long? LowestLiveCursor(NeuronId publisher)
    {
        var cutoff = os.Clock.GetUtcNow() - os.Options.StaleCursorAfter;
        lock (_gate)
        {
            var live = _bySubscriber.Values.Select(sources => sources.GetValueOrDefault(publisher)).Where(entry => entry is not null && entry.CursorAt > cutoff).Select(entry => entry!.Cursor).ToList();
            return live.Count == 0 ? null : live.Min();
        }
    }

    // Subscribers whose cursor on some source is below that source's last sequence.
    public IReadOnlyList<NeuronId> Behind()
    {
        lock (_gate)
        {
            return [.. _bySubscriber.Where(pair => pair.Value.Values.Any(entry => entry.Cursor < os.Journal(entry.Source.Brain).Last(entry.Source))).Select(pair => pair.Key)];
        }
    }

    // On every activation the subscriber catches up on each source from its cursor.
    public void Activated(TestNeuronHost subscriber)
    {
        List<NeuronId> sources;
        lock (_gate) { sources = _bySubscriber.TryGetValue(subscriber.Id, out var held) ? [.. held.Keys] : []; }
        foreach (var source in sources) { _ = os.Track(Deliver(subscriber, source, long.MaxValue, reference: false)); }
    }

    // The journal entry is written: ring the doorbell of every active subscriber with one reference per signal (twice under
    // DuplicateDelivery). An inactive subscriber is not woken; it catches up when it activates.
    public void Appended(NeuronId publisher, IReadOnlyList<JournaledSignal> written)
    {
        if (written.Count == 0) { return; }
        if (os.TakePublishFault(publisher))
        {
            throw new InvalidOperationException("TestOS.FailNextPublishAfterAppend: the append is durable, no reference was sent.");
        }
        os.Listeners.Appended(publisher, written);
        List<NeuronId> subscribers;
        lock (_gate) { subscribers = [.. _bySubscriber.Where(pair => pair.Value.ContainsKey(publisher)).Select(pair => pair.Key)]; }
        var copies = os.Options.DuplicateDelivery ? 2 : 1;
        foreach (var subscriber in subscribers)
        {
            if (!os.TryHost(subscriber, out var host) || !host.IsActive) { continue; }
            foreach (var entry in written)
            {
                for (var copy = 0; copy < copies; copy++) { _ = os.Track(Deliver(host, publisher, entry.Sequence, reference: true)); }
            }
        }
    }

    // One subscriber's references are handled one at a time: a reference at or below the cursor is a duplicate and dropped;
    // above it, the subscriber reads the source's journal from the cursor through the reference, each signal its own turn.
    private async Task Deliver(TestNeuronHost subscriber, NeuronId source, long through, bool reference)
    {
        await Task.Yield();
        var pump = _pumps.GetOrAdd(subscriber.Id, _ => new SemaphoreSlim(1, 1));
        await pump.WaitAsync().ConfigureAwait(false);
        try
        {
            Entry? entry;
            lock (_gate) { entry = _bySubscriber.GetValueOrDefault(subscriber.Id)?.GetValueOrDefault(source); }
            if (entry is null) { return; }
            if (reference && through <= entry.Cursor)
            {
                Interlocked.Increment(ref _duplicates);
                return;
            }
            await CatchUp(subscriber, entry, through).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { os.Fail(error); }
        finally { pump.Release(); }
    }

    private async Task CatchUp(TestNeuronHost subscriber, Entry entry, long through)
    {
        var journal = os.Journal(entry.Source.Brain);
        while (true)
        {
            var page = await journal.Read(entry.Source, entry.Cursor, SignalPage.MaxLimit, CancellationToken.None).ConfigureAwait(false);
            if (page.Floor > entry.Cursor)
            {
                await React(subscriber, new Gap(entry.Source, entry.Cursor, page.Floor), entry, page.Floor).ConfigureAwait(false);
                Advance(entry, page.Floor);
                continue;
            }
            var due = page.Signals.Where(item => item.Sequence <= through).ToList();
            if (due.Count == 0) { return; }
            foreach (var item in due)
            {
                bool wanted;
                lock (_gate) { wanted = entry.Signals.Any(type => type.IsInstanceOfType(item.Signal)); }
                if (wanted) { await React(subscriber, item.Signal, entry, item.Sequence).ConfigureAwait(false); }
                Advance(entry, item.Sequence);
            }
        }
    }

    // A journal write of a reacting subscriber carries its cursor (R4-09): the state, the signals and the advance land in one step with
    // no await between them, so a stop after the reaction's save never runs the reaction again.
    public void Committed(NeuronId writer)
    {
        if (Reacting.Value is { } reaction && reaction.Subscriber == writer) { Advance(reaction.Entry, reaction.Cursor); }
    }

    private void Advance(Entry entry, long cursor)
    {
        lock (_gate)
        {
            entry.Cursor = cursor;
            entry.CursorAt = os.Clock.GetUtcNow();
        }
    }

    // A reaction is a turn: the most specific [On] method whose parameter accepts the signal; none means the signal is ignored.
    private Task React(TestNeuronHost subscriber, Signal signal, Entry entry, long cursor)
    {
        if (signal is not Gap)
        {
            os.NoteDelivery(new SignalDelivery(entry.Source.ToString(), subscriber.Id.ToString(), AliasOf(signal.GetType()), cursor));
        }
        if (Find(subscriber.Class, signal.GetType()) is not { } method) { return Task.CompletedTask; }
        return subscriber.Turn(interleave: false, async neuron =>
        {
            Reacting.Value = new Reaction(subscriber.Id, entry, cursor);
            var arguments = method.GetParameters().Length == 2 ? new object?[] { signal, CancellationToken.None } : [signal];
            object? returned;
            try { returned = method.Invoke(neuron, arguments); }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
            if (returned is Task task) { await task.ConfigureAwait(false); }
            else if (returned is ValueTask valueTask) { await valueTask.ConfigureAwait(false); }
            return 0;
        });
    }

    private static MethodInfo? Find(Type neuronClass, Type signal) => Reactions.GetOrAdd((neuronClass, signal), static key =>
    {
        var candidates = new List<MethodInfo>();
        for (var type = key.Class; type is not null && type != typeof(object); type = type.BaseType)
        {
            candidates.AddRange(type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.IsDefined(typeof(OnAttribute), false) && method.GetParameters() is [var first, ..] && first.ParameterType.IsAssignableFrom(key.Signal)));
        }
        return candidates.OrderByDescending(method => Depth(method.GetParameters()[0].ParameterType)).FirstOrDefault();
    });

    private static int Depth(Type type)
    {
        var depth = 0;
        for (var current = type; current is not null; current = current.BaseType) { depth++; }
        return depth;
    }

    private sealed record Reaction(NeuronId Subscriber, Entry Entry, long Cursor);

    private sealed class Entry(NeuronId source, NeuronId subscriber)
    {
        public NeuronId Source { get; } = source;

        public NeuronId Subscriber { get; } = subscriber;

        public HashSet<Type> Signals { get; } = [];

        public long Cursor { get; set; }

        public DateTimeOffset CursorAt { get; set; }
    }
}
