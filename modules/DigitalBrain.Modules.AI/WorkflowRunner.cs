using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Security;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans.Concurrency;
using Orleans.Serialization;

namespace DigitalBrain.AI;

[Alias("ai.workflow-runner")]
internal interface IWorkflowRunner : IGrainWithStringKey
{
    [OneWay]
    [Alias("Execute")]
    Task ExecuteAsync(WorkflowRunCommand command);
}

[Alias("ai.workflow-run-owner")]
internal interface IWorkflowRunOwner : IGrainWithStringKey
{
    [Alias("AuthorizeParticipant")]
    Task<CapabilityDelegation> AuthorizeParticipantAsync(
        WorkflowRun run,
        OrchestrationParticipant participant);

    [Alias("AuthorizeCompletion")]
    Task<CapabilityDelegation> AuthorizeCompletionAsync(WorkflowRun run);
}

[Alias("ai.workflow-run-completion")]
internal interface IWorkflowRunCompletion : INeuron
{
    [Alias("Complete")]
    Task<bool> CompleteAsync(WorkflowRunResult result);
}

[GrainType("ai-workflow-runner")]
internal sealed class WorkflowRunner(
    IGrainFactory grains,
    IDurablePayloadProtector payloadProtector,
    ILogger<WorkflowRunner> logger,
    Serializer<ChatMessage> messages) : Grain, IWorkflowRunner
{
    private const string CheckpointProtectionPurpose = "DigitalBrain.AI.WorkflowCheckpoint.v1";
    private static readonly Action<ILogger, Guid, Exception?> LogRunFailure = LoggerMessage.Define<Guid>(
        LogLevel.Error,
        new EventId(1, "WorkflowRunFailed"),
        "Workflow run {RunId} failed before adoption.");
    private Guid? _executing;

    public async Task ExecuteAsync(WorkflowRunCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_executing == command.Run.RunId)
        {
            return;
        }

        if (_executing is not null)
        {
            throw new InvalidOperationException(
                $"Workflow runner '{this.GetGrainId()}' is already executing another run.");
        }

        _executing = command.Run.RunId;

        try
        {
            await ExecuteCoreAsync(command);
        }
        catch (Exception failure)
        {
            LogRunFailure(logger, command.Run.RunId, failure);

            throw;
        }
        finally
        {
            _executing = null;
        }
    }

    private async Task ExecuteCoreAsync(WorkflowRunCommand command)
    {
        RequireCommandMatchesRunner(command);
        var turnScheduler = TaskScheduler.Current;
        var owner = grains.GetGrain<IWorkflowRunOwner>(command.Run.Cursor.Worker.ToGrainId());
        var participants = MafParticipantAdapter.CreateDelegated(
            grains,
            command.Definition.Participants,
            turnScheduler,
            participant => OnTurn(
                () => owner.AuthorizeParticipantAsync(command.Run, participant),
                turnScheduler));
        var workflow = GroupChatWorkflow.Create(participants);
        var identity = WorkflowCheckpointIdentity.For(command.Run.Cursor);
        var checkpointGrain = grains.GetGrain<IWorkflowCheckpointGrain>(
            IdSpan.Create(identity.GrainKey));
        var protectionPurpose = $"{CheckpointProtectionPurpose}\n{identity.SessionId}";
        var store = new OrleansCheckpointStore(
            checkpointGrain,
            identity.SessionId,
            payloadProtector,
            protectionPurpose);
        var checkpoints = CheckpointManager.CreateJson(store);
        StreamingRun execution;

        if (command.Run.InputCheckpoint is null)
        {
            execution = await InProcessExecution.Lockstep
                .WithCheckpointing(checkpoints)
                .RunStreamingAsync(
                    workflow,
                    ChatMessageCopies.Clone(command.ReplayInput, messages),
                    identity.SessionId,
                    CancellationToken.None);

            if (!await execution.TrySendMessageAsync(new TurnToken(emitEvents: true)))
            {
                throw new InvalidOperationException("The fresh workflow run rejected its initial turn token.");
            }
        }
        else
        {
            RequireCheckpointIdentity(command.Run.InputCheckpoint, identity);
            execution = await InProcessExecution.Lockstep
                .WithCheckpointing(checkpoints)
                .ResumeStreamingAsync(
                    workflow,
                    new CheckpointInfo(
                        command.Run.InputCheckpoint.SessionId,
                        command.Run.InputCheckpoint.CheckpointId),
                    CancellationToken.None);
        }

        List<ChatMessage>? terminal = null;
        CheckpointInfo? outputCheckpoint = null;

        try
        {
            await foreach (var workflowEvent in execution.WatchStreamAsync(CancellationToken.None))
            {
                if (workflowEvent is WorkflowOutputEvent output
                    && !output.IsIntermediate()
                    && output.Is<List<ChatMessage>>(out var messages))
                {
                    terminal = messages;
                }

                if (workflowEvent is SuperStepCompletedEvent completed)
                {
                    outputCheckpoint = completed.CompletionInfo?.Checkpoint;
                    break;
                }
            }
        }
        finally
        {
            try
            {
                await execution.CancelRunAsync();
            }
            finally
            {
                await execution.DisposeAsync();
            }
        }

        if (outputCheckpoint is null)
        {
            throw new InvalidOperationException("The workflow superstep completed without a durable checkpoint.");
        }

        var result = new WorkflowRunResult(
            command.Run,
            new WorkflowCheckpointReference(
                outputCheckpoint.SessionId,
                outputCheckpoint.CheckpointId),
            terminal is null ? null : ChatMessageCopies.Clone(terminal, messages));
        var completionDelegation = await OnTurn(
            () => owner.AuthorizeCompletionAsync(command.Run),
            turnScheduler);
        var completion = grains.GetGrain<IWorkflowRunCompletion>(
            command.Run.Cursor.Worker.ToGrainId());

        _ = await OnTurn(
            () => DigitalBrainRuntime.InvokeAsync(
                completionDelegation,
                () => completion.CompleteAsync(result)),
            turnScheduler);
    }

    private void RequireCommandMatchesRunner(WorkflowRunCommand command)
    {
        var expected = WorkflowRunnerIdentity.GrainKey(command.Run);

        if (!string.Equals(this.GetPrimaryKeyString(), expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Workflow run '{command.Run.RunId}' was dispatched to the wrong runner identity.");
        }

        if (!string.Equals(
                command.Run.DefinitionFingerprint,
                command.Definition.Fingerprint,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The workflow run definition does not match its command snapshot.");
        }
    }

    private static void RequireCheckpointIdentity(
        WorkflowCheckpointReference checkpoint,
        WorkflowCheckpointIdentity identity)
    {
        if (!string.Equals(checkpoint.SessionId, identity.SessionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The workflow input checkpoint does not belong to its stable Worker/Task/Attempt lineage.");
        }
    }

    private static Task<T> OnTurn<T>(Func<Task<T>> invoke, TaskScheduler turnScheduler)
        => Task.Factory.StartNew(
            invoke,
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach,
            turnScheduler).Unwrap();
}

internal static class WorkflowRunnerIdentity
{
    internal static string GrainKey(WorkflowRun run)
        => $"{run.Cursor.Worker.GrainKey}/workflow-run/{run.RunId:N}";
}
