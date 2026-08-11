using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Execution;

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
        ValidateExecution(envelope.Request.Execution);
        if (envelope.Request.Worker != envelope.Worker)
        {
            throw new NeuronAuthorizationException("worker-mismatch");
        }

        return SendAsync(envelope.Worker, new DispatchWorkerAccept(envelope.Request));
    }

    public Task HandleAsync(RelayWorkerContinue envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateWorker(envelope.Worker);
        ValidateExecution(envelope.Cursor.Execution);
        if (envelope.Cursor.Worker != envelope.Worker)
        {
            throw new NeuronAuthorizationException("worker-mismatch");
        }

        return SendAsync(envelope.Worker, new DispatchWorkerContinue(envelope.Cursor));
    }

    public Task HandleAsync(RelayWorkerCancel envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateWorker(envelope.Worker);
        ValidateExecution(envelope.Cursor.Execution);
        if (envelope.Cursor.Worker != envelope.Worker)
        {
            throw new NeuronAuthorizationException("worker-mismatch");
        }

        return SendAsync(envelope.Worker, new DispatchWorkerCancel(envelope.Cursor));
    }

    private void ValidateWorker(NeuronId worker)
    {
        if (worker == default
            || string.IsNullOrWhiteSpace(worker.Type)
            || string.IsNullOrWhiteSpace(worker.Name))
        {
            throw new NeuronAuthorizationException("invalid-worker-identity");
        }

        if (worker.Owner != Id.Owner)
        {
            throw new NeuronAuthorizationException(
                $"Relay '{Id}' cannot dispatch to worker '{worker}' owned by '{worker.Owner}'.");
        }

        // Domain adapters (e.g. chat-turn-worker) are first-class workers. The legacy
        // IWorker grain-type pin rejected every production adapter that is not the
        // harness "worker" type — identity is owner + non-empty type/name only.
    }

    private void ValidateExecution(NeuronId execution)
    {
        if (execution == default
            || string.IsNullOrWhiteSpace(execution.Type)
            || string.IsNullOrWhiteSpace(execution.Name))
        {
            throw new NeuronAuthorizationException("invalid-execution-identity");
        }

        if (execution.Owner != Id.Owner)
        {
            throw new NeuronAuthorizationException(
                $"Relay '{Id}' cannot carry execution '{execution}' owned by '{execution.Owner}'.");
        }

        if (!string.Equals(
                execution.Type,
                NeuronId.GrainTypeNameOf(typeof(IExecution)),
                StringComparison.OrdinalIgnoreCase)
            && !string.Equals(execution.Type, "execution", StringComparison.OrdinalIgnoreCase))
        {
            throw new NeuronAuthorizationException("invalid-execution-identity");
        }
    }
}
