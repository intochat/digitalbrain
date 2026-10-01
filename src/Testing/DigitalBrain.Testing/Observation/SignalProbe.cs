using System.Threading.Channels;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
namespace DigitalBrain.Testing;

public sealed class SignalProbe<T> : IAsyncDisposable where T : Signal
{
    private readonly ISignalSubscription<T> _subscription;
    private readonly CancellationTokenSource _cancel = new();
    private readonly Channel<T> _pending;
    private readonly Queue<T> _recent = new();
    private readonly Lock _gate = new();
    private readonly int _buffer;
    private readonly Task _worker;
    private int _disposed;
    private readonly TestExecutionOptions _execution;
    internal SignalProbe(ISignalSubscription<T> subscription, int buffer, TestExecutionOptions execution)
    {
        _execution = execution;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(buffer);
        _subscription = subscription;
        _buffer = buffer;
        _pending = Channel.CreateBounded<T>(buffer);
        _worker = ObserveAsync();
    }
    public IReadOnlyList<T> Snapshot { get { lock (_gate) { return _recent.ToArray(); } } }
    private async Task ObserveAsync()
    {
        try
        {
            await foreach (var value in _subscription.ReadAllAsync(_cancel.Token).ConfigureAwait(false))
            {
                lock (_gate)
                {
                    if (_recent.Count == _buffer) { _recent.Dequeue(); }
                    _recent.Enqueue(value);
                }
                if (!_pending.Writer.TryWrite(value)) { throw new InvalidOperationException("Signal probe buffer overflowed."); }
            }
            _pending.Writer.TryComplete();
        }
        catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { _pending.Writer.TryComplete(); }
        catch (Exception error) { _pending.Writer.TryComplete(error); }
    }
    public async Task<T> NextAsync(Func<T, bool>? predicate = null, CancellationToken ct = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_execution.AssertionTimeout);
        try
        {
            while (true)
            {
                var next = await _pending.Reader.ReadAsync(deadline.Token).ConfigureAwait(false);
                if (predicate is null || predicate(next)) { return next; }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"No matching {typeof(T).Name} within {_execution.AssertionTimeout}; observed {Snapshot.Count} facts (payloads omitted).");
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
        await _cancel.CancelAsync().ConfigureAwait(false);
        try { await _worker.WaitAsync(_execution.CleanupTimeout).ConfigureAwait(false); }
        finally { await _subscription.DisposeAsync().ConfigureAwait(false); _cancel.Dispose(); }
    }
}
