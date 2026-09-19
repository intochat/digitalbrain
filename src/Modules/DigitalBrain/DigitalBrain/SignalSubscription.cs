using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DigitalBrain.Contracts;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Core;

public sealed class SignalSubscription<T> : ISignalSubscription<T>, INeuronObserver where T : Signal
{
    private readonly IGrainFactory _grains;
    private readonly INeuron _source;
    private readonly BrainOptions _options;
    private readonly ILogger _logger;
    private readonly Action<IAsyncDisposable> _closed;
    private readonly LocalSignalHub? _hub;
    private readonly Channel<T> _messages;
    private readonly CancellationTokenSource _lifetime;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _gate = new();
    private INeuronObserver? _reference;
    private Task<Guid>? _watch;
    private Task _renewal = Task.CompletedTask;
    private Task? _cleanup;
    private int _reader;

    internal SignalSubscription(IGrainFactory grains, INeuron source, BrainOptions options,
        ILogger logger, Action<IAsyncDisposable> closed, LocalSignalHub? hub, CancellationToken cancellationToken)
    {
        _grains = grains;
        _source = source;
        _options = options;
        _logger = logger;
        _closed = closed;
        _hub = hub;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _messages = Channel.CreateBounded<T>(new BoundedChannelOptions(options.BufferCapacity) { SingleReader = true });
        _ = _completion.Task.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public Task Completion => _completion.Task;

    internal async Task ConnectAsync()
    {
        try
        {
            var token = _lifetime.Token;
            if (_hub is not null)
            {
                _hub.Subscribe(_source.GetGrainId(), this);
                _logger.LogInformation("SubscriptionReady {Source} {SignalType}", _source.GetGrainId(), typeof(T).Name);
                return;
            }
            _reference = _grains.CreateObjectReference<INeuronObserver>(this);
            var activation = await WatchAsync(token).ConfigureAwait(false);
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                _renewal = RenewAsync(activation, token);
            }
            _logger.LogInformation("SubscriptionReady {Source} {SignalType}", _source.GetGrainId(), typeof(T).Name);
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
            _watch = _source.Watch(_reference!);
            return _watch.WaitAsync(_options.OperationTimeout, token);
        }
    }

    private async Task RenewAsync(Guid activation, CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(_options.RenewEvery, token).ConfigureAwait(false);
                if (await WatchAsync(token).ConfigureAwait(false) != activation)
                { throw new InvalidOperationException("Neuron reactivated; subscribe again. Live signals may have been missed."); }
            }
        }
        catch (Exception error) { Finish(error); }
        finally { await CleanupAsync().ConfigureAwait(false); }
    }

    Task INeuronObserver.OnSignalAsync(Signal signal)
    {
        lock (_gate)
        {
            if (!Completion.IsCompleted && signal is T typed && !_messages.Writer.TryWrite(typed))
            { Finish(new InvalidOperationException("Signal subscription buffer overflowed.")); }
        }
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
            else if (error is not null) { _completion.SetException(error); }
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
            _hub?.Unsubscribe(_source.GetGrainId(), this);
            if (_reference is null) { return; }
            if (_watch is { IsCompleted: false }) { _ = CleanupLateWatchAsync(_watch); }
            else { _ = _watch?.Exception; }
            try { await UnwatchAsync().ConfigureAwait(false); }
            finally { _grains.DeleteObjectReference<INeuronObserver>(_reference); }
        }
        finally
        {
            _lifetime.Dispose();
            _closed(this);
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
        try { await _source.Unwatch(_reference!).WaitAsync(_options.OperationTimeout).ConfigureAwait(false); }
        catch (Exception error) { _logger.LogWarning(error, "Subscription cleanup failed; remote membership expires with its lease."); }
    }
}
