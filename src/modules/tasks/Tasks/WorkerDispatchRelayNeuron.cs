using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.Tasks;

[GrainType(WorkerDispatchRelay.GrainTypeName)]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Orleans grain activated by the silo from GrainType metadata.")]
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
        ValidateTask(envelope.Request.Task);
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
        ValidateTask(envelope.Cursor.Task);
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
        ValidateTask(envelope.Cursor.Task);
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

        if (!string.Equals(
                worker.Type,
                NeuronId.GrainTypeNameOf(typeof(IWorker)),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new NeuronAuthorizationException("invalid-worker-identity");
        }
    }

    private void ValidateTask(NeuronId task)
    {
        if (task == default
            || string.IsNullOrWhiteSpace(task.Type)
            || string.IsNullOrWhiteSpace(task.Name))
        {
            throw new NeuronAuthorizationException("invalid-task-identity");
        }

        if (task.Owner != Id.Owner)
        {
            throw new NeuronAuthorizationException(
                $"Relay '{Id}' cannot carry task '{task}' owned by '{task.Owner}'.");
        }

        if (!string.Equals(
                task.Type,
                NeuronId.GrainTypeNameOf(typeof(ITask)),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new NeuronAuthorizationException("invalid-task-identity");
        }
    }
}
