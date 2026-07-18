using System.Collections.Concurrent;
using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Kernel.Gateway;

public sealed class GatewayCorrelationTracker
{
    static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    readonly ConcurrentDictionary<Guid, TaskCompletionSource<Synapse>> _pending = new();

    public Awaiter Track(Guid correlationId, CancellationToken ct, TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource<Synapse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(correlationId, tcs))
            throw new InvalidOperationException(
                $"Correlation {correlationId} is already being tracked; concurrent reuse violates request/response semantics.");

        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout ?? DefaultTimeout);
        var registration = linked.Token.Register(() => tcs.TrySetCanceled(linked.Token));

        return new Awaiter(correlationId, tcs.Task, () =>
        {
            registration.Dispose();
            linked.Dispose();
            _pending.TryRemove(correlationId, out _);
        });
    }

    public void Complete(Synapse synapse)
    {
        if (_pending.TryGetValue(synapse.CorrelationId, out var tcs))
            tcs.TrySetResult(synapse);
    }

    public sealed class Awaiter(Guid correlationId, Task<Synapse> task, Action dispose) : IDisposable
    {
        public Guid CorrelationId { get; } = correlationId;
        public Task<Synapse> Task { get; } = task;
        public void Dispose() => dispose();
    }
}
