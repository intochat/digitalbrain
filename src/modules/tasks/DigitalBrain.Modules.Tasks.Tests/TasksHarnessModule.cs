using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.Tasks.Tests;

public sealed partial class TasksHarnessModule : IModule;

[GrainType(GrainTypeName)]
internal sealed class ScriptedWorker :
    Neuron,
    IWorker,
    IHandle<PrepareOperationProbe>,
    IHandle<TransitionOperationProbe>,
    IHandle<TaskOperationSnapshot>
{
    internal const string GrainTypeName = "worker";

    public async Task Accept(AttemptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        await SendAsync(
            request.Task,
            new AttemptAccepted(request.Task, request.Worker, request.Attempt, request.Revision));

        switch (request.Goal)
        {
            case RetryableFailureGoal when request.Revision == 0:
                await SendAsync(
                    request.Task,
                    new AttemptFailed(
                        request.Task,
                        request.Worker,
                        request.Attempt,
                        request.Revision,
                        TaskFixtures.Retryable,
                        Retryable: true));
                return;

            case SuccessGoal:
                await SendAsync(
                    request.Task,
                    new AttemptSucceeded(
                        request.Task,
                        request.Worker,
                        request.Attempt,
                        request.Revision,
                        TaskFixtures.Done,
                        Evidence:
                        [
                            new FactReference(request.Worker, SynapseId.New()),
                        ]));
                return;

            case StaleProbeGoal:
                await SendAsync(
                    request.Task,
                    new AttemptSucceeded(
                        request.Task,
                        request.Worker,
                        request.Attempt,
                        request.Revision + 1,
                        TaskFixtures.StaleSuccess,
                        Evidence: []));
                return;
        }
    }

    public Task Continue(AttemptCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        return Task.CompletedTask;
    }

    public Task Cancel(AttemptCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        return SendAsync(cursor.Task, new AttemptCancelled(cursor.Task, cursor.Worker, cursor.Attempt, cursor.Revision));
    }

    public async Task HandleAsync(PrepareOperationProbe probe, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);
        cancellationToken.ThrowIfCancellationRequested();

        await SendAsync(
            probe.Task,
            new PrepareTaskOperation(probe.Attempt, probe.Sequence, probe.Edge, probe.RequestPayload));
    }

    public async Task HandleAsync(TransitionOperationProbe probe, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);
        cancellationToken.ThrowIfCancellationRequested();

        await SendAsync(
            probe.Task,
            new TransitionTaskOperation(
                probe.Attempt,
                probe.Sequence,
                probe.ExpectedPhase,
                probe.Phase,
                probe.ResponsePayload,
                RedactedSummary: null));
    }

    public Task HandleAsync(TaskOperationSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
