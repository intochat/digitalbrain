using System.Collections.Concurrent;
using System.Reflection;
using DigitalBrain.Kernel;
using DigitalBrain.Neurons;
using DigitalBrain.Units;

namespace DigitalBrain.Testing;

internal sealed class SynapseRegistry : INudge
{
    internal const string BrainWide = "*";
    internal static NeuronId BrainWideOf(BrainId brain) => new(brain, BrainWide, BrainWide);

    private readonly TestOS _os;
    private readonly ConcurrentDictionary<BrainId, Fanout> _fanouts = new();
    private readonly ConcurrentDictionary<NeuronId, Delivery> _deliveries = new();
    private readonly DeliveryCounters _counters;

    public long DuplicatesDropped => _counters.DuplicatesDropped;

    public SynapseRegistry(TestOS os)
    {
        _os = os;
        _counters = new DeliveryCounters((_, _, _, error) => _os.Fail(error));
    }

    public Delivery For(NeuronId subscriber)
        => _deliveries.GetOrAdd(subscriber, id =>
        {
            var journal = _os.Journal(id.Brain);
            var consumer = new TestConsumer(_os, id);
            var delivery = new Delivery(
                id,
                journal,
                journal.MemoryJournal,
                consumer,
                alias => _os.TypeOf(alias),
                _os.Clock,
                _counters,
                delivery => _os.NoteDelivery(delivery));
            delivery.Initialize().GetAwaiter().GetResult();
            return delivery;
        });

    public Fanout FanoutFor(BrainId brain)
        => _fanouts.GetOrAdd(brain, b =>
        {
            var journal = _os.Journal(b).MemoryJournal;
            return new Fanout(
                journal,
                this,
                alias => _os.TypeOf(alias),
                (pub, signals) =>
                {
                    if (_os.TakePublishFault(pub))
                    {
                        throw new InvalidOperationException("FailNextPublishAfterAppend");
                    }
                    _os.Listeners.Offered(pub, signals);
                });
        });

    public ISignalNudge NudgeFor(BrainId brain) => FanoutFor(brain);

    public void Nudge(NeuronId subscriber, Position upTo)
    {
        var view = _os.Journal(subscriber.Brain).MemoryJournal;
        var subs = view.Subscriptions.All.FirstOrDefault(s => s.Subscriber == subscriber);
        var isBrainWide = subs?.Entries.Exists(e => e.IsBrainWide) == true;
        if (!isBrainWide && !_os.IsActive(subscriber)) { return; }
        if (!_os.TryHost(subscriber, out _)) { return; }
        var d = For(subscriber);
        var copies = _os.Options.DuplicateDelivery ? 2 : 1;
        for (var i = 0; i < copies; i++)
        {
            _os.Track(Task.Run(async () =>
            {
                await d.Nudged(upTo).ConfigureAwait(false);
            }));
        }
    }

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

        IReadOnlyList<Type> named = [.. signals];
        var source = _os.Host(from);
        var request = new SynapseRequest(subscriber.Id, named, _os.Caller);
        await _os.Track(source.Turn(interleave: false, async neuron =>
        {
            await NeuronAttachment.Admit(neuron, request).ConfigureAwait(false);
            return 0;
        })).ConfigureAwait(false);

        var aliases = signals.Select(SignalAliases.Of).ToArray();
        await For(subscriber.Id).Ensure(from, aliases, _os.Clock.GetUtcNow()).ConfigureAwait(false);
    }

    public async Task EnsureBrainWide(NeuronId subscriber, Type[] signals, bool exact = false, IReadOnlyList<string>? aliases = null)
    {
        var stringAliases = (aliases ?? []).Concat(signals.Select(SignalAliases.Of)).Distinct(StringComparer.Ordinal).ToArray();
        await For(subscriber).EnsureBrainWide(stringAliases, _os.Clock.GetUtcNow(), exact).ConfigureAwait(false);
    }

    public async Task EnsureBrainWide(TestNeuronHost subscriber, Type[] signals, bool exact = false, IReadOnlyList<string>? aliases = null)
    {
        if (subscriber.Class.IsDefined(typeof(StatelessNeuronAttribute), false))
        {
            throw new InvalidOperationException($"{subscriber.Class.FullName} is a [StatelessNeuron] and holds no synapses.");
        }
        await EnsureBrainWide(subscriber.Id, signals, exact, aliases).ConfigureAwait(false);
    }

    public async Task Remove(NeuronId subscriber, NeuronId from)
    {
        var d = For(subscriber);
        if (from.Key == BrainWide)
        {
            await d.RemoveBrainWide().ConfigureAwait(false);
        }
        else
        {
            await d.Remove(from).ConfigureAwait(false);
        }
    }

    public void Activated(TestNeuronHost host)
    {
        _os.Track(Task.Run(async () =>
        {
            await For(host.Id).CatchUp().ConfigureAwait(false);
        }));
    }

    public void RemoveSubscriber(NeuronId subscriber)
    {
        if (_deliveries.TryRemove(subscriber, out var delivery))
        {
            delivery.Dispose();
        }
    }

    public void Appended(NeuronId publisher, IReadOnlyList<JournaledSignal> written)
    {
        if (_os.TakePublishFault(publisher)) { return; }
        _os.Listeners.Appended(publisher, written);
        var view = _os.Journal(publisher.Brain).MemoryJournal;
        FanoutFor(publisher.Brain).Appended(publisher, [.. written.Select(s => SignalAliases.Of(s.Signal))], view.Last);
    }

    public static void Committed(NeuronId id) { _ = id; }

    public IReadOnlyList<Synapse> Of(NeuronId subscriber)
    {
        var view = _os.Journal(subscriber.Brain).MemoryJournal;
        var subs = view.Subscriptions.All.FirstOrDefault(s => s.Subscriber == subscriber);
        if (subs is null) { return []; }
        var list = new List<Synapse>();
        foreach (var entry in subs.Entries)
        {
            var from = entry.Source ?? BrainWideOf(subscriber.Brain);
            foreach (var alias in entry.Aliases)
            {
                var type = _os.TypeOf(alias);
                if (type is not null)
                {
                    list.Add(new Synapse(from, type, subscriber));
                }
            }
        }
        return list.OrderBy(s => s.From.ToString(), StringComparer.Ordinal).ThenBy(s => s.Signal.FullName, StringComparer.Ordinal).ToList();
    }

    public ObservedSynapse[] All()
    {
        var list = new List<ObservedSynapse>();
        foreach (var b in _os.AllBrains)
        {
            var view = _os.Journal(b).MemoryJournal;
            foreach (var subs in view.Subscriptions.All)
            {
                foreach (var entry in subs.Entries)
                {
                    var from = entry.Source?.ToString() ?? BrainWide;
                    foreach (var alias in entry.Aliases)
                    {
                        list.Add(new ObservedSynapse(from, subs.Subscriber.ToString(), alias));
                    }
                }
            }
        }
        return [.. list];
    }

    public long? Cursor(NeuronId subscriber, NeuronId source)
    {
        var view = _os.Journal(subscriber.Brain).MemoryJournal;
        var subs = view.Subscriptions.All.FirstOrDefault(s => s.Subscriber == subscriber);
        if (subs is null) { return null; }
        var isWide = source.Key == BrainWide;
        var entry = subs.Entries.FirstOrDefault(e => isWide ? e.Source is null : e.Source == source);
        if (entry is null) { return null; }
        return isWide ? entry.Cursor.Value : view.SequenceAt(source, entry.Cursor);
    }

    public long? LowestLiveCursor(NeuronId publisher)
    {
        var view = _os.Journal(publisher.Brain).MemoryJournal;
        var cutoff = _os.Clock.GetUtcNow() - _os.Options.StaleCursorAfter;
        return view.Subscriptions.LowestLive(publisher, cutoff)?.Value;
    }

    public IReadOnlyList<NeuronId> Behind()
    {
        var list = new List<NeuronId>();
        foreach (var b in _os.AllBrains)
        {
            var view = _os.Journal(b).MemoryJournal;
            list.AddRange(view.Subscriptions.Due(view, alias => _os.TypeOf(alias), slack: 0));
        }
        return list;
    }

    public IAdmission Admission => new TestAdmission(_os);

    private sealed class TestAdmission(TestOS os) : IAdmission
    {
        public async Task Admit(NeuronId subscriber, NeuronId publisher, string[] signalAliases, CancellationToken ct = default)
        {
            var signals = signalAliases.Select(os.TypeOf).Where(t => t is not null).Cast<Type>().ToArray();
            var source = os.Host(publisher);
            var request = new SynapseRequest(subscriber, signals, os.Caller);
            await os.Track(source.Turn(interleave: false, async neuron =>
            {
                await NeuronAttachment.Admit(neuron, request).ConfigureAwait(false);
                return 0;
            })).ConfigureAwait(false);
        }
    }

    private sealed class TestConsumer(TestOS os, NeuronId subscriber) : ISignalConsumer
    {
        public bool TakesUntyped => typeof(ISynapseReceiver).IsAssignableFrom(os.Host(subscriber).Class);

        public async Task Consume(JournaledSignal signal, Func<IDisposable> openScope, CancellationToken ct)
        {
            var host = os.Host(subscriber);
            await os.Track(host.Turn(interleave: false, async neuron =>
            {
                using var scope = openScope();
                TestCause.Enter(signal.Publisher, signal.Sequence, signal.TraceId);
                await Reactions.Dispatch(neuron, signal, ct).ConfigureAwait(false);
                return 0;
            })).ConfigureAwait(false);
        }
    }
}
