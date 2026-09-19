using System.Collections.Concurrent;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Testing;

public sealed class BehaviorRun : IAsyncDisposable
{
    private readonly CancellationTokenSource _cancel;
    private readonly ObservedBrain _brain;
    private int _disposed;
    internal BehaviorRun(IDigitalBrain brain, Func<IDigitalBrain, CancellationToken, Task> body, CancellationToken ct)
    {
        _cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _brain = new(brain);
        Completion = Task.Run(() => body(_brain, _cancel.Token), CancellationToken.None);
    }
    public Task Completion { get; }
    public async Task WaitForSubscriptionAsync<T>(INeuron source, CancellationToken ct = default) where T : Signal
    {
        var ready = _brain.Ready(source.GetGrainId(), typeof(T)).Task;
        await Task.WhenAny(ready, Completion).WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        if (!ready.IsCompletedSuccessfully)
        {
            await Completion.ConfigureAwait(false);
            throw new InvalidOperationException("Behavior ended before establishing the requested subscription.");
        }
        await ready.ConfigureAwait(false);
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
        await _cancel.CancelAsync().ConfigureAwait(false);
        try { await Completion.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { }
        finally { _cancel.Dispose(); }
    }
    private sealed class ObservedBrain(IDigitalBrain inner) : IDigitalBrain
    {
        private readonly ConcurrentDictionary<(GrainId, Type), TaskCompletionSource> _ready = new();
        internal TaskCompletionSource Ready(GrainId source, Type type)
            => _ready.GetOrAdd((source, type), _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        public T Get<T>(string id) where T : class, IGrainWithStringKey => inner.Get<T>(id);
        public async Task<SignalSubscription<T>> SubscribeAsync<T>(INeuron source, CancellationToken cancellationToken = default) where T : Signal
        {
            var subscription = await inner.SubscribeAsync<T>(source, cancellationToken).ConfigureAwait(false);
            Ready(source.GetGrainId(), typeof(T)).TrySetResult();
            return subscription;
        }
    }
}
