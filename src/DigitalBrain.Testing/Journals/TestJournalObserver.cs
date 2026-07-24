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
    private const int ChannelCapacity = 64;

    private readonly Channel<JournalObservation> _observations =
        Channel.CreateBounded<JournalObservation>(
            new BoundedChannelOptions(ChannelCapacity)
            {
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            });
    private readonly SemaphoreSlim _pushGate = new(1, 1);
    private readonly JournalKind _direction;
    private long _cursor;
    private int _disposed;

    internal TestJournalObserver(JournalKind direction)
    {
        _direction = direction;
    }

    internal ChannelReader<JournalObservation> Observations
        => _observations.Reader;

    public async Task ObserveAsync(JournalKind kind, JournalRead read)
    {
        ArgumentNullException.ThrowIfNull(read);

        if (kind != _direction)
        {
            throw new InvalidOperationException(
                $"A {_direction} journal observer received a {kind} batch.");
        }

        await _pushGate.WaitAsync();
        try
        {
            var observation = new JournalObservation(_cursor, read);
            _cursor = read.ResumeSequence;
            await _observations.Writer.WriteAsync(observation);
        }
        finally
        {
            _pushGate.Release();
        }
    }

    internal void Complete(Exception? failure = null)
        => _observations.Writer.TryComplete(failure);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Complete();

        await _pushGate.WaitAsync();
        _pushGate.Release();
        _pushGate.Dispose();
    }
}
