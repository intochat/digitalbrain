using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.Tasks;

[GrainType(WorkerDispatchRelay.GrainTypeName)]
internal sealed class WorkerDispatchRelayNeuron :
    Neuron,
    IHandle<RelayWorkerAccept>,
    IHandle<RelayWorkerContinue>,
    IHandle<RelayWorkerCancel>
{
    public Task HandleAsync(RelayWorkerAccept envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateWorker(envelope.Worker);
        if (envelope.Request.Worker != envelope.Worker)
        {
            throw new InvalidOperationException("worker-mismatch");
        }

        return SendAsync(envelope.Worker, new DispatchWorkerAccept(envelope.Request));
    }

    public Task HandleAsync(RelayWorkerContinue envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateWorker(envelope.Worker);
        if (envelope.Cursor.Worker != envelope.Worker)
        {
            throw new InvalidOperationException("worker-mismatch");
        }

        return SendAsync(envelope.Worker, new DispatchWorkerContinue(envelope.Cursor));
    }

    public Task HandleAsync(RelayWorkerCancel envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateWorker(envelope.Worker);
        if (envelope.Cursor.Worker != envelope.Worker)
        {
            throw new InvalidOperationException("worker-mismatch");
        }

        return SendAsync(envelope.Worker, new DispatchWorkerCancel(envelope.Cursor));
    }

    private void ValidateWorker(NeuronId worker)
    {
        if (worker == default
            || string.IsNullOrWhiteSpace(worker.Type)
            || string.IsNullOrWhiteSpace(worker.Name))
        {
            throw new InvalidOperationException("invalid-worker-identity");
        }

        if (worker.Owner != Id.Owner)
        {
            throw new NeuronAuthorizationException(
                $"Relay '{Id}' cannot dispatch to worker '{worker}' owned by '{worker.Owner}'.");
        }

        if (!string.Equals(
                worker.Type,
                NeuronId.GrainTypeNameOf(typeof(IWorker)),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("invalid-worker-identity");
        }
    }
}
