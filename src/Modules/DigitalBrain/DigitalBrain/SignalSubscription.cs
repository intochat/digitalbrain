using System.Threading.Channels;
using DigitalBrain.Contracts;

namespace DigitalBrain.Core;

public sealed class SignalSubscription<T> : IAsyncDisposable where T : Signal
{
    private readonly Channel<T> _messages;
    private readonly Func<ValueTask> _cleanup;
    private int _disposed;
    internal SignalSubscription(int capacity, Func<ValueTask> cleanup)
    {
        _cleanup = cleanup;
        _messages = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait,
        });
    }
    public Task Completion => _messages.Reader.Completion;
    public IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken = default)
        => _messages.Reader.ReadAllAsync(cancellationToken);
    internal void Hear(Signal signal)
    {
        if (signal is T typed && !_messages.Writer.TryWrite(typed))
        {
            _messages.Writer.TryComplete(new InvalidOperationException("Signal subscription buffer overflowed."));
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
        _messages.Writer.TryComplete();
        await _cleanup().ConfigureAwait(false);
    }
}
