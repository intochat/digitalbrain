using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

public abstract partial class Neuron
{
    public Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence)
        => Task.FromResult(FeedFor(kind).Read(afterSequence));

    public async Task Watch(JournalKind kind, long afterSequence, IJournalObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        _ = FeedFor(kind);
        _watchers.RemoveAll(existing => existing.Kind == kind && existing.Observer.Equals(observer));

        var watcher = new Watcher(observer, kind, afterSequence);
        _watchers.Add(watcher);

        await PushAsync(watcher).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public Task Unwatch(IJournalObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        _watchers.RemoveAll(existing => existing.Observer.Equals(observer));

        return Task.CompletedTask;
    }

    private async Task PushAsync(Watcher watcher)
    {
        var read = FeedFor(watcher.Kind).Read(watcher.Cursor);

        if (read.Delta.Count == 0 && read.ResetSnapshot is null)
        {
            return;
        }

        watcher.Cursor = read.ResumeSequence;

        await watcher.Observer.ObserveAsync(watcher.Kind, read).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "An observer that cannot be reached is a disconnected client, not a fault of this neuron. Dropping it is the recovery, and the client resumes with its cursor.")]
    private async Task NotifyWatchersAsync()
    {
        foreach (var watcher in _watchers.ToArray())
        {
            try
            {
                await PushAsync(watcher).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }
            catch (Exception unreachable)
            {
                _watchers.Remove(watcher);

                SynapseTelemetry.WatcherDropped(Id, unreachable);
            }
        }
    }

    private sealed class Watcher(IJournalObserver observer, JournalKind kind, long cursor)
    {
        public IJournalObserver Observer { get; } = observer;

        public JournalKind Kind { get; } = kind;

        public long Cursor { get; set; } = cursor;
    }

    private NeuronFeed FeedFor(JournalKind kind) => kind switch
    {
        JournalKind.Incoming => _incoming,
        JournalKind.Outgoing => _outgoing,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

}
