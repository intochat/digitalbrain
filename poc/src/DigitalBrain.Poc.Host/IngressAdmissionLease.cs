using DigitalBrain.Poc.Abstractions;

namespace DigitalBrain.Poc.Host;

public sealed class IngressAdmissionLease : IAsyncDisposable
{
    private readonly IngressQuiesceGate _gate;
    private readonly Func<Synapse, CancellationToken, Task> _enqueue;
    private int _state;

    internal IngressAdmissionLease(
        IngressQuiesceGate gate,
        Func<Synapse, CancellationToken, Task> enqueue)
    {
        _gate = gate;
        _enqueue = enqueue;
    }

    public async Task FireAsync(
        Synapse synapse,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            throw new InvalidOperationException("An ingress admission lease can be consumed only once.");
        }

        _gate.TransferRegistrationToQueuedTurn();
        try
        {
            await _enqueue(synapse, cancellationToken);
        }
        finally
        {
            _gate.CompleteQueuedTurn();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
        {
            _gate.ReleaseUnusedRegistration();
        }

        return ValueTask.CompletedTask;
    }
}
