using System.Threading.Channels;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

internal sealed record JournalObservation(
    long RequestedCursor,
    JournalRead Read);

internal sealed class TestJournalObserver :
    IJournalObserver,
    IAsyncDisposable
{
    internal const int EvidenceLimit = 64;

    private readonly Channel<JournalObservation> _observations =
        Channel.CreateBounded<JournalObservation>(
            new BoundedChannelOptions(EvidenceLimit)
            {
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            });
    private readonly Lock _gate = new();
    private readonly JournalKind _direction;

    private bool _completed;
    private Exception? _completionFailure;
    private long _cursor;
    private int _disposed;

    internal TestJournalObserver(JournalKind direction)
    {
        _direction = direction;
    }

    internal ChannelReader<JournalObservation> Observations
        => _observations.Reader;

    public Task ObserveAsync(JournalKind kind, JournalRead read)
    {
        ArgumentNullException.ThrowIfNull(read);

        if (kind != _direction)
        {
            throw new InvalidOperationException(
                $"A {_direction} journal observer received a {kind} batch.");
        }

        lock (_gate)
        {
            if (_completed)
            {
                return Task.FromException(
                    _completionFailure
                    ?? new ObjectDisposedException(nameof(TestJournalObserver)));
            }

            var observation = new JournalObservation(_cursor, read);
            if (_observations.Writer.TryWrite(observation))
            {
                _cursor = read.ResumeSequence;
                return Task.CompletedTask;
            }

            var failure = new InvalidOperationException(
                $"Journal evidence overflow for direction '{_direction}': capacity {EvidenceLimit} was exhausted for the batch requested after cursor {_cursor}, with resume sequence {read.ResumeSequence} and {read.Delta.Count} deliveries.");
            CompleteUnderLock(failure);
            return Task.FromException(failure);
        }
    }

    internal void Complete(Exception? failure = null)
    {
        lock (_gate)
        {
            if (_completed)
            {
                return;
            }

            CompleteUnderLock(failure);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        Complete();

        return ValueTask.CompletedTask;
    }

    private void CompleteUnderLock(Exception? failure)
    {
        _completed = true;
        _completionFailure = failure;
        _observations.Writer.TryComplete(failure);
    }
}
