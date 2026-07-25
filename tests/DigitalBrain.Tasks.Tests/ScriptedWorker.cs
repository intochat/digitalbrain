using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.Tasks.Tests;

[GrainType("worker")]
internal sealed class ScriptedWorker : Neuron, IWorker
{
    public async Task Accept(AttemptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        await SendAsync(
            request.Task,
            new AttemptAccepted(
                request.Task,
                request.Worker,
                request.Attempt,
                request.Revision));

        if (request.Goal is RetryableFailureGoal && request.Revision == 0)
        {
            await SendAsync(
                request.Task,
                new AttemptFailed(
                    request.Task,
                    request.Worker,
                    request.Attempt,
                    request.Revision,
                    new TestFailure("retryable"),
                    Retryable: true));
            return;
        }

        if (request.Goal is SuccessGoal)
        {
            await SendAsync(
                request.Task,
                new AttemptSucceeded(
                    request.Task,
                    request.Worker,
                    request.Attempt,
                    request.Revision,
                    new TestResult("done"),
                    Evidence:
                    [
                        new FactReference(request.Worker, SynapseId.New()),
                    ]));
            return;
        }

        if (request.Goal is not StaleProbeGoal)
        {
            return;
        }

        await SendAsync(
            request.Task,
            new AttemptSucceeded(
                request.Task,
                request.Worker,
                request.Attempt,
                request.Revision + 1,
                new TestResult("stale-success"),
                Evidence: []));
    }

    public Task Continue(AttemptCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        return Task.CompletedTask;
    }

    public Task Cancel(AttemptCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        return SendAsync(
            cursor.Task,
            new AttemptCancelled(
                cursor.Task,
                cursor.Worker,
                cursor.Attempt,
                cursor.Revision));
    }
}
