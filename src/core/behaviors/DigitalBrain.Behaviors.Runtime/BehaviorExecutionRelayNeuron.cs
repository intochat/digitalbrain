using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Behaviors.Runtime;

[GrainType(BehaviorExecutionRelay.GrainTypeName)]
internal sealed class BehaviorExecutionRelayNeuron :
    Neuron,
    IHandle<RelayHostedBehaviorExecution>,
    IHandle<RunHostedBehaviorExecution>
{
    // First hop returns immediately so the Worker's outbox drain is not held across hosted I/O.
    // DrainAsync awaits Deliver on the emitting grain; awaiting Execute here would re-block Worker.
    public Task HandleAsync(RelayHostedBehaviorExecution envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateWorker(envelope.Worker);
        ValidateTask(envelope.Attempt.Task);
        if (envelope.Attempt.Worker != envelope.Worker)
        {
            throw new NeuronAuthorizationException("worker-mismatch");
        }

        return SendAsync(
            Id,
            new RunHostedBehaviorExecution(
                envelope.Worker,
                envelope.Attempt,
                envelope.Execution,
                envelope.UtcNow));
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Relay maps any hosted execution failure to a stable terminal code for the Worker.")]
    public async Task HandleAsync(RunHostedBehaviorExecution envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateWorker(envelope.Worker);
        ValidateTask(envelope.Attempt.Task);
        if (envelope.Attempt.Worker != envelope.Worker)
        {
            throw new NeuronAuthorizationException("worker-mismatch");
        }

        if (envelope.Attempt.Goal is not BehaviorActivationGoal goal)
        {
            throw new NeuronAuthorizationException("behavior-goal-required");
        }

        var request = new BehaviorExecutionRequest(
            new BehaviorExecutionMetadata(
                envelope.Worker.Owner,
                goal.BehaviorId,
                goal.Revision,
                envelope.Execution),
            ArtifactBytes: ReadOnlyMemory<byte>.Empty,
            goal.Revision.Value,
            envelope.Attempt.Task,
            envelope.Attempt.Attempt,
            goal.TriggerTypeName,
            goal.ProtectedPayload,
            ToCapabilityEdges(goal.Capabilities),
            envelope.UtcNow,
            envelope.Worker);

        BehaviorExecutionOutcome outcome;
        var cancelled = false;
        // Link request/turn token with process-local attempt CTS (never durable).
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            BehaviorAttemptCancellation.TokenOrNone(envelope.Attempt.Task, envelope.Attempt.Attempt));
        try
        {
            var executor = ServiceProvider.GetRequiredService<IBehaviorExecutor>();
            outcome = await executor.ExecuteAsync(request, linked.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            outcome = new BehaviorExecutionOutcome(false, BehaviorExecutionCodes.Cancelled);
        }
        catch (BehaviorUserActionRequiredException userActionRequired)
        {
            var requirement = userActionRequired.Requirement
                ?? throw new InvalidOperationException(
                    "User action required exception is missing the bound requirement.");
            if (!MatchesAttempt(requirement, envelope.Attempt))
            {
                outcome = new BehaviorExecutionOutcome(false, BehaviorExecutionCodes.Exception);
            }
            else
            {
                outcome = new BehaviorExecutionOutcome(
                    false,
                    BehaviorExecutionCodes.UserActionRequired,
                    BehaviorUserActionSurface.FromRequirement(requirement));
            }
        }
        catch (BehaviorHostException exception)
        {
            outcome = new BehaviorExecutionOutcome(
                false,
                BehaviorExecutionCodes.MapHostFailure(exception.Reason));
        }
        catch (Exception exception)
        {
            _ = exception;
            outcome = new BehaviorExecutionOutcome(false, BehaviorExecutionCodes.Exception);
        }

        var stableCode = cancelled
            ? BehaviorExecutionCodes.Cancelled
            : outcome.Succeeded
                ? BehaviorExecutionCodes.Succeeded
                : BehaviorExecutionCodes.MapHostFailure(outcome.Outcome);

        if (!outcome.Succeeded
            && BehaviorExecutionCodes.IsInProcessClosed(outcome.Outcome))
        {
            stableCode = BehaviorExecutionCodes.InProcessClosed;
        }

        UserActionRequired? userAction = null;
        if (!outcome.Succeeded
            && string.Equals(stableCode, BehaviorExecutionCodes.UserActionRequired, StringComparison.Ordinal))
        {
            if (outcome.UserAction is null
                || !MatchesAttempt(outcome.UserAction, envelope.Attempt))
            {
                // Bare stable-code-only or mismatched identity must not strand Running as a park.
                stableCode = BehaviorExecutionCodes.Exception;
            }
            else
            {
                userAction = outcome.UserAction.ToRequirement();
            }
        }

        await SendAsync(
            envelope.Worker,
            new CompleteHostedBehaviorExecution(
                envelope.Attempt,
                outcome.Succeeded && !cancelled,
                stableCode,
                cancelled,
                userAction));
    }

    private static bool MatchesAttempt(UserActionRequired requirement, AttemptRequest attempt)
        => requirement.Task == attempt.Task
            && requirement.Attempt == attempt.Attempt
            && requirement.ParkRevision == attempt.Revision
            && requirement.ActionEpoch != Guid.Empty
            && requirement.Completer != default
            && requirement.Module != default;

    private static bool MatchesAttempt(BehaviorUserActionSurface surface, AttemptRequest attempt)
        => surface.Task == attempt.Task
            && surface.Attempt == attempt.Attempt
            && surface.ParkRevision == attempt.Revision
            && surface.ActionEpoch != Guid.Empty
            && surface.Completer != default
            && surface.Module != default;

    private static BehaviorCapabilityEdge[] ToCapabilityEdges(IReadOnlyList<TaskOperationEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);
        var result = new BehaviorCapabilityEdge[edges.Count];
        for (var index = 0; index < edges.Count; index++)
        {
            var edge = edges[index];
            result[index] = new BehaviorCapabilityEdge(
                edge.Target,
                edge.RequestSynapseId,
                edge.RequestSchemaVersion,
                edge.ResponseSynapseId,
                edge.ResponseSchemaVersion);
        }

        return result;
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
