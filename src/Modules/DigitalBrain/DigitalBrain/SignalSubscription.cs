using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DigitalBrain.Contracts;
namespace DigitalBrain.Core;

public sealed class SignalSubscription<T> : IAsyncDisposable where T : Signal
{
    private readonly Channel<T> _messages;
    private readonly CancellationTokenSource _lifetime;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task _worker = Task.CompletedTask;
    private int _reader;
    private int _disposed;

    internal SignalSubscription(int capacity, CancellationToken cancellationToken)
    {
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _messages = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
        _ = _completion.Task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    public Task Completion => _completion.Task;
    internal Task Ready => _ready.Task;
    internal void Registered() => _ready.TrySetResult();
    internal void Start(Func<CancellationToken, Task> run) => _worker = RunAsync(run);
    private async Task RunAsync(Func<CancellationToken, Task> run)
    {
        try { await run(_lifetime.Token).ConfigureAwait(false); Stop(null); }
        catch (Exception error) { Stop(error); _ready.TrySetException(error); }
    }
    public async IAsyncEnumerable<T> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _reader, 1) != 0) { throw new InvalidOperationException("A subscription has one reader."); }
        try
        {
            await foreach (var signal in _messages.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return signal;
            }
        }
        finally { await DisposeAsync().ConfigureAwait(false); }
    }
    internal void Hear(Signal signal)
    {
        if (signal is T typed && !Completion.IsCompleted && !_messages.Writer.TryWrite(typed))
        {
            Stop(new InvalidOperationException("Signal subscription buffer overflowed."));
        }
    }
    private void Stop(Exception? error)
    {
        if (error is OperationCanceledException canceled) { _completion.TrySetCanceled(canceled.CancellationToken); }
        else if (error is not null) { _completion.TrySetException(error); }
        else { _completion.TrySetResult(); }
        _messages.Writer.TryComplete(error);
        _lifetime.Cancel();
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { await _worker.ConfigureAwait(false); return; }
        Stop(null);
        await _worker.ConfigureAwait(false);
        _lifetime.Dispose();
    }
}
