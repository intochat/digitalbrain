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

    [AlwaysInterleave]
    [Alias("Cancel")]
    Task CancelAsync(Guid runId);
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
    private static readonly Action<ILogger, Guid, Exception?> LogRunFailure = LoggerMessage.Define<Guid>(
        LogLevel.Error,
        new EventId(1, "WorkflowRunFailed"),
        "Workflow run {RunId} failed before adoption.");
    private Guid? _executing;
    private CancellationTokenSource? _executionCancellation;

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

        var cancellation = new CancellationTokenSource();
        _executing = command.Run.RunId;
        _executionCancellation = cancellation;

        try
        {
            await ExecuteCoreAsync(command, cancellation.Token);
        }
        catch (Exception failure)
        {
            LogRunFailure(logger, command.Run.RunId, failure);

            throw;
        }
        finally
        {
            if (_executing == command.Run.RunId
                && ReferenceEquals(_executionCancellation, cancellation))
            {
                _executing = null;
                _executionCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    public Task CancelAsync(Guid runId)
    {
        if (_executing == runId)
        {
            _executionCancellation?.Cancel();
        }

        return Task.CompletedTask;
    }

    private async Task ExecuteCoreAsync(
        WorkflowRunCommand command,
        CancellationToken cancellationToken)
    {
        RequireCommandMatchesRunner(command);
        cancellationToken.ThrowIfCancellationRequested();
        var turnScheduler = TaskScheduler.Current;
        var owner = grains.GetGrain<IWorkflowRunOwner>(command.Run.Cursor.Worker.ToGrainId());
        var participants = MafParticipantAdapter.CreateDelegated(
            grains,
            command.Definition.Participants,
            turnScheduler,
            participant => OnTurn(
                () => owner.AuthorizeParticipantAsync(command.Run, participant),
                turnScheduler,
                cancellationToken));
        var workflow = GroupChatWorkflow.Create(participants);
        var identity = WorkflowCheckpointIdentity.For(command.Run.Cursor);
        var checkpointGrain = grains.GetGrain<IWorkflowCheckpointGrain>(
            IdSpan.Create(identity.GrainKey));
        var protectionPurpose = WorkflowCheckpointProtection.Purpose(
            identity.SessionId,
            command.Run.DefinitionFingerprint);
        var store = new OrleansCheckpointStore(
            checkpointGrain,
            identity.SessionId,
            payloadProtector,
            protectionPurpose);
        var checkpoints = CheckpointManager.CreateJson(store);
        StreamingRun execution;
        var inputCheckpoint = command.Run.InputCheckpoint;
        var sendInitialTurn = inputCheckpoint is null;

        if (inputCheckpoint is null)
        {
            execution = await InProcessExecution.Lockstep
                .WithCheckpointing(checkpoints)
                .RunStreamingAsync(
                    workflow,
                    ChatMessageCopies.Clone(command.ReplayInput, messages),
                    identity.SessionId,
                    cancellationToken);
        }
        else
        {
            RequireCheckpointIdentity(inputCheckpoint, identity);
            execution = await InProcessExecution.Lockstep
                .WithCheckpointing(checkpoints)
                .ResumeStreamingAsync(
                    workflow,
                    new CheckpointInfo(
                        inputCheckpoint.SessionId,
                        inputCheckpoint.CheckpointId),
                    cancellationToken);
        }

        List<ChatMessage>? terminal = null;
        CheckpointInfo? outputCheckpoint = null;

        try
        {
            if (sendInitialTurn)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var accepted = await AwaitSendAsync(
                    execution.TrySendMessageAsync(new TurnToken(emitEvents: true)),
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (!accepted)
                {
                    throw new InvalidOperationException("The fresh workflow run rejected its initial turn token.");
                }
            }

            await foreach (var workflowEvent in execution.WatchStreamAsync(cancellationToken))
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
            await AwaitCleanupAsync(execution, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

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
            turnScheduler,
            cancellationToken);
        var completion = grains.GetGrain<IWorkflowRunCompletion>(
            command.Run.Cursor.Worker.ToGrainId());

        _ = await OnTurn(
            () => DigitalBrainRuntime.InvokeAsync(
                completionDelegation,
                () => completion.CompleteAsync(result)),
            turnScheduler,
            cancellationToken);
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

    private static Task<T> OnTurn<T>(
        Func<Task<T>> invoke,
        TaskScheduler turnScheduler,
        CancellationToken cancellationToken)
    {
        var pending = Task.Factory.StartNew(
            invoke,
            cancellationToken,
            TaskCreationOptions.DenyChildAttach,
            turnScheduler).Unwrap();

        return AwaitOperationAsync(pending, cancellationToken);
    }

    private static Task<bool> AwaitSendAsync(
        ValueTask<bool> pending,
        CancellationToken cancellationToken)
        => AwaitOperationAsync(pending.AsTask(), cancellationToken);

    private static Task AwaitCleanupAsync(
        StreamingRun execution,
        CancellationToken cancellationToken)
        => AwaitOperationAsync(CleanupExecutionAsync(execution), cancellationToken);

    private static async Task CleanupExecutionAsync(StreamingRun execution)
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

    private static async Task AwaitOperationAsync(
        Task pending,
        CancellationToken cancellationToken)
    {
        ObserveLateFault(pending);

        await pending.WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static async Task<T> AwaitOperationAsync<T>(
        Task<T> pending,
        CancellationToken cancellationToken)
    {
        ObserveLateFault(pending);

        var result = await pending.WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static void ObserveLateFault(Task pending)
    {
        _ = pending.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }
}

internal static class WorkflowRunnerIdentity
{
    internal static string GrainKey(WorkflowRun run)
        => $"{run.Cursor.Worker.GrainKey}/workflow-run/{run.RunId:N}";
}
