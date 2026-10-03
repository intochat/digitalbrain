using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DigitalBrain;
using DigitalBrain.Contracts;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Client;

internal sealed class SignalSubscription<T>(IClusterClient cluster, INeuron source, BrainOptions options,
    ILogger logger, Action<IAsyncDisposable> closed, ILocalSignalHub? hub, CancellationToken cancellationToken)
    : ISignalSubscription<T>, INeuronObserver where T : Signal
{
    private readonly Channel<T> _messages = Channel.CreateBounded<T>(new BoundedChannelOptions(options.BufferCapacity) { SingleReader = true });
    private readonly CancellationTokenSource _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _gate = new();
    private INeuronObserver? _reference;
    private Task<Guid>? _watch;
    private Task _renewal = Task.CompletedTask;
    private Task? _cleanup;
    private int _reader;

    public Task Completion => _completion.Task;

    internal async Task ConnectAsync()
    {
        try
        {
            var token = _lifetime.Token;
            // Only the remote path renews; the caller's token must end a silo-local subscription too.
            token.Register(() => Finish(new OperationCanceledException(token)));
            if (hub is not null) { hub.Subscribe(source.GetGrainId(), this); }
            else
            {
                _reference = cluster.CreateObjectReference<INeuronObserver>(this);
                var activation = await WatchAsync(token).ConfigureAwait(false);
                lock (_gate)
                {
                    token.ThrowIfCancellationRequested();
                    _renewal = RenewAsync(activation, token);
                }
            }
            logger.LogInformation("SubscriptionReady {Source} {SignalType}", source.GetGrainId(), typeof(T).Name);
        }
        catch (Exception error)
        {
            Finish(error);
            await CleanupAsync().ConfigureAwait(false);
            throw;
        }
    }

    private Task<Guid> WatchAsync(CancellationToken token)
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            _watch = source.Watch(_reference!);
            return _watch.WaitAsync(options.OperationTimeout, token);
        }
    }

    private async Task RenewAsync(Guid activation, CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(options.RenewEvery, token).ConfigureAwait(false);
                if (await WatchAsync(token).ConfigureAwait(false) != activation)
                { throw new InvalidOperationException("Neuron reactivated; subscribe again. Live signals may have been missed."); }
            }
        }
        catch (Exception error) { Finish(error); }
        finally { await CleanupAsync().ConfigureAwait(false); }
    }

    // The channel writer is thread-safe, and a completed writer rejects late signals, so no lock is needed.
    Task INeuronObserver.OnSignalAsync(Signal signal)
    {
        if (signal is T typed && !_messages.Writer.TryWrite(typed))
        { Finish(new InvalidOperationException("Signal subscription buffer overflowed.")); }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<T> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _reader, 1) != 0) { throw new InvalidOperationException("A subscription has one reader."); }
        try
        {
            await foreach (var signal in _messages.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            { yield return signal; }
        }
        finally { await DisposeAsync().ConfigureAwait(false); }
    }

    private void Finish(Exception? error)
    {
        lock (_gate)
        {
            if (Completion.IsCompleted) { return; }
            _messages.Writer.TryComplete(error);
            if (error is OperationCanceledException canceled) { _completion.SetCanceled(canceled.CancellationToken); }
            else if (error is not null)
            {
                _completion.SetException(error);
                // Observed here so an unread failure never surfaces as an unobserved task exception.
                _ = _completion.Task.Exception;
            }
            else { _completion.SetResult(); }
            _lifetime.Cancel();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Finish(null);
        await _renewal.ConfigureAwait(false);
        await CleanupAsync().ConfigureAwait(false);
    }

    private Task CleanupAsync()
    {
        lock (_gate) { return _cleanup ??= CleanupCoreAsync(); }
    }

    private async Task CleanupCoreAsync()
    {
        try
        {
            hub?.Unsubscribe(source.GetGrainId(), this);
            if (_reference is null) { return; }
            if (_watch is { IsCompleted: false }) { _ = CleanupLateWatchAsync(_watch); }
            else { _ = _watch?.Exception; }
            try { await UnwatchAsync().ConfigureAwait(false); }
            finally { cluster.DeleteObjectReference<INeuronObserver>(_reference); }
        }
        finally
        {
            _lifetime.Dispose();
            closed(this);
        }
    }

    private async Task CleanupLateWatchAsync(Task<Guid> watch)
    {
        try { await watch.ConfigureAwait(false); }
        catch { return; }
        await UnwatchAsync().ConfigureAwait(false);
    }

    private async Task UnwatchAsync()
    {
        try { await source.Unwatch(_reference!).WaitAsync(options.OperationTimeout).ConfigureAwait(false); }
        catch (Exception error) { logger.LogWarning(error, "Subscription cleanup failed; remote membership expires with its lease."); }
    }
}
