using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Messaging;
namespace DigitalBrain.Core;

internal sealed class NeuronJournal
{
    private const string IncomingName = "incoming";
    private const string OutgoingName = "outgoing";

    private readonly Neuron _neuron;
    private readonly NeuronFeed _incoming;
    private readonly NeuronFeed _outgoing;
    private readonly List<Watcher> _watchers = [];

    internal NeuronJournal(Neuron neuron, IServiceProvider services)
    {
        _neuron = neuron;
        _incoming = new NeuronFeed(services, IncomingName);
        _outgoing = new NeuronFeed(services, OutgoingName);
    }

    internal long OutgoingNextSequence => _outgoing.NextSequence;

    internal JournalRead Read(JournalKind kind, long afterSequence)
        => FeedFor(kind).Read(afterSequence);

    internal async Task WatchAsync(
        JournalKind kind,
        long afterSequence,
        IJournalObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        _ = FeedFor(kind);
        _watchers.RemoveAll(existing =>
            existing.Kind == kind && existing.Observer.Equals(observer));

        var watcher = new Watcher(observer, kind, afterSequence);
        _watchers.Add(watcher);

        await PushAsync(watcher)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    internal void Unwatch(IJournalObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _watchers.RemoveAll(existing => existing.Observer.Equals(observer));
    }

    internal NeuronFeedCheckpoint IncomingCheckpoint() => _incoming.Checkpoint();

    internal NeuronFeedCheckpoint OutgoingCheckpoint() => _outgoing.Checkpoint();

    internal void AppendIncoming(SynapseDelivery delivery) => _incoming.Append(delivery);

    internal void AppendOutgoing(SynapseDelivery delivery) => _outgoing.Append(delivery);

    internal void RestoreIncoming(NeuronFeedCheckpoint checkpoint) => _incoming.Restore(checkpoint);

    internal void RestoreOutgoing(NeuronFeedCheckpoint checkpoint) => _outgoing.Restore(checkpoint);

    internal async Task NotifyWatchersAsync()
    {
        foreach (var watcher in _watchers.ToArray())
        {
            try
            {
                await PushAsync(watcher)
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }
            catch (Exception unreachable)
            {
                _watchers.Remove(watcher);
                SynapseTelemetry.WatcherDropped(_neuron.Id, unreachable);
            }
        }
    }

    private async Task PushAsync(Watcher watcher)
    {
        var read = FeedFor(watcher.Kind).Read(watcher.Cursor);

        if (read.Delta.Count == 0 && read.ResetSnapshot is null)
        {
            return;
        }

        watcher.Cursor = read.ResumeSequence;

        await watcher.Observer.ObserveAsync(watcher.Kind, read)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private NeuronFeed FeedFor(JournalKind kind) => kind switch
    {
        JournalKind.Incoming => _incoming,
        JournalKind.Outgoing => _outgoing,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private sealed class Watcher(IJournalObserver observer, JournalKind kind, long cursor)
    {
        internal IJournalObserver Observer { get; } = observer;

        internal JournalKind Kind { get; } = kind;

        internal long Cursor { get; set; } = cursor;
    }
}
