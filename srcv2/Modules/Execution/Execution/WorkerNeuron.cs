using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Execution;

// Translates durable dispatch envelopes into IWorker verbs for concrete workers.
public abstract class WorkerNeuron :
    Neuron,
    IWorker,
    IHandle<DispatchWorkerAccept>,
    IHandle<DispatchWorkerContinue>,
    IHandle<DispatchWorkerCancel>
{
    public abstract Task Accept(AttemptRequest request);

    public abstract Task Continue(AttemptCursor cursor);

    public abstract Task Cancel(AttemptCursor cursor);

    public Task HandleAsync(DispatchWorkerAccept envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        return Accept(envelope.Request);
    }

    public Task HandleAsync(DispatchWorkerContinue envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        return Continue(envelope.Cursor);
    }

    public Task HandleAsync(DispatchWorkerCancel envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        return Cancel(envelope.Cursor);
    }
}
