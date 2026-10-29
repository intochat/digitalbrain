using System.Collections.Concurrent;
using DigitalBrain.Operations;
using DigitalBrain.Spi;

namespace DigitalBrain.Testing.Conformance.Storage;

/// <summary>Lets a conformance kit wait for the result of an operation a storage started.</summary>
public interface IOperationsProbe
{
    /// <summary>Completes with the operation's result once it ends.</summary>
    /// <exception cref="KeyNotFoundException">No operation has this id.</exception>
    Task<OperationResult> WaitFor(OperationId id, CancellationToken ct);
}

/// <summary>One recorded step of an operation: <c>started</c>, <c>progress</c> or <c>done</c>.</summary>
/// <param name="Id">The operation.</param>
/// <param name="Stage"><c>started</c>, <c>progress</c> or <c>done</c>.</param>
/// <param name="Fraction">The progress fraction, for <c>progress</c>.</param>
/// <param name="Result">The result, for <c>done</c>.</param>
public sealed record InMemoryOperationEvent(OperationId Id, string Stage, double? Fraction, OperationResult? Result);

/// <summary>
/// Operations held in memory: each started plan records <c>started</c>, its progress and <c>done</c>, and
/// <see cref="WaitFor"/> completes when the operation completes. Cancellation fires the handle's token.
/// </summary>
public sealed class InMemoryOperations : IOperations, IOperationsProbe
{
    private readonly ConcurrentDictionary<OperationId, Handle> _operations = new();
    private readonly ConcurrentQueue<InMemoryOperationEvent> _events = new();
    private int _next;

    /// <summary>Every recorded step, in order.</summary>
    public IReadOnlyList<InMemoryOperationEvent> Events => [.. _events];

    /// <summary>The plan of an operation.</summary>
    /// <exception cref="KeyNotFoundException">No operation has this id.</exception>
    public OperationPlan PlanOf(OperationId id) => Get(id).Plan;

    /// <inheritdoc />
    public Task<IOperation> Start(OperationPlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var handle = new Handle(this, new OperationId($"mop-{Interlocked.Increment(ref _next)}"), plan);
        _operations[handle.Id] = handle;
        _events.Enqueue(new InMemoryOperationEvent(handle.Id, "started", null, null));
        return Task.FromResult<IOperation>(handle);
    }

    /// <inheritdoc />
    /// <exception cref="KeyNotFoundException">No operation has this id.</exception>
    public async Task Cancel(OperationId id, CancellationToken ct) => await Get(id).Cancel().ConfigureAwait(false);

    /// <inheritdoc />
    public Task<OperationResult> WaitFor(OperationId id, CancellationToken ct) => Get(id).Done.Task.WaitAsync(ct);

    private Handle Get(OperationId id) =>
        _operations.TryGetValue(id, out var handle) ? handle : throw new KeyNotFoundException($"No operation {id}.");

    private sealed class Handle(InMemoryOperations owner, OperationId id, OperationPlan plan) : IOperation, IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Lock _completionGate = new();

        public OperationId Id { get; } = id;

        public OperationPlan Plan { get; } = plan;

        public TaskCompletionSource<OperationResult> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken Cancellation => _cancellation.Token;

        public void Dispose() => _cancellation.Dispose();

        public ValueTask Report(double fraction)
        {
            owner._events.Enqueue(new InMemoryOperationEvent(Id, "progress", Math.Clamp(fraction, 0, 1), null));
            return ValueTask.CompletedTask;
        }

        public ValueTask Complete(OperationResult result)
        {
            ArgumentNullException.ThrowIfNull(result);
            lock (_completionGate)
            {
                if (!Done.Task.IsCompleted)
                {
                    owner._events.Enqueue(new InMemoryOperationEvent(Id, "done", null, result));
                    Done.SetResult(result);
                }
            }
            return ValueTask.CompletedTask;
        }

        public async Task Cancel()
        {
            await _cancellation.CancelAsync().ConfigureAwait(false);
            await Complete(new OperationResult.Cancelled()).ConfigureAwait(false);
        }
    }
}
