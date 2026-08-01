using DigitalBrain.Abstractions;
using Xunit;

namespace DigitalBrain.Tasks.Tests;

public sealed partial class TaskLifecycle
{
    [Fact(DisplayName = "Cancel moves Cancelling then AttemptCancelled to Cancelled")]
    public async Task CancelMovesThroughCancellingToCancelled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var (worker, task, started) = await StartAsync(brain, "cancel", new TestGoal("cancel-me"));
        var running = await AcceptThenRunningAsync(task, cancellationToken);

        var cancel = new CancelTask(CommandId.New(), running.Revision);
        var cancelling = await task.Reference.Cancel(cancel);

        Assert.Equal(TaskState.Cancelling, cancelling.State);
        Assert.Equal(running.Revision, cancelling.Revision);
        Assert.Equal(running.ActiveAttempt, cancelling.ActiveAttempt);
        AssertReceipt(cancelling, await task.Reference.Cancel(cancel));

        var cancelledFact = await task.Incoming.NextAsync<AttemptCancelled>(cancellationToken);
        AssertAttempt(cancelledFact, task.Id, worker.Id, running.ActiveAttempt, running.Revision);

        var cancelled = await WaitForStateAsync(task, TaskState.Cancelled, cancellationToken);

        Assert.Null(cancelled.ActiveAttempt);
        Assert.Null(cancelled.Blocker);
        Assert.Equal(started.Goal, cancelled.Goal);
    }

    [Fact(DisplayName =
        "Cancel with an in-flight Dispatched operation becomes OutcomeUncertain without Cancelled and without a second provider effect")]
    public async Task CancelWithDispatchedOperationBecomesOutcomeUncertain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var (worker, task, _) = await StartAsync(brain, "cancel-dispatched", new TestGoal("cancel-dispatched"));
        var running = await AcceptThenRunningAsync(task, cancellationToken);
        Assert.NotNull(running.ActiveAttempt);
        var attempt = running.ActiveAttempt.Value;
        var edge = new TaskOperationEdge(
            new NeuronId("provider", task.Id.Owner, "gmail"),
            "test.provider-request",
            RequestSchemaVersion: 1,
            "test.provider-response",
            ResponseSchemaVersion: 1);
        var request = new ProtectedPayloadReference(
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));

        var preparedWait = worker.Incoming.NextAsync<TaskOperationSnapshot>(cancellationToken);
        await brain.Client.SendAsync<IWorker>(
            worker.Id.Name,
            new PrepareOperationProbe(task.Id, attempt, Sequence: 0, edge, request),
            cancellationToken);
        Assert.Equal(TaskOperationPhase.Prepared, (await preparedWait).Synapse.Phase);

        var dispatchedWait = worker.Incoming.NextAsync<TaskOperationSnapshot>(cancellationToken);
        await brain.Client.SendAsync<IWorker>(
            worker.Id.Name,
            new TransitionOperationProbe(
                task.Id,
                attempt,
                Sequence: 0,
                TaskOperationPhase.Prepared,
                TaskOperationPhase.Dispatched,
                ResponsePayload: null),
            cancellationToken);
        Assert.Equal(TaskOperationPhase.Dispatched, (await dispatchedWait).Synapse.Phase);

        var outcomeWait = task.Outgoing.NextAsync<AttemptOutcomeUncertain>(cancellationToken);
        var cancelled = await task.Reference.Cancel(new CancelTask(CommandId.New(), running.Revision));

        Assert.Equal(TaskState.Waiting, cancelled.State);
        var blocker = Assert.IsType<OutcomeUncertain>(cancelled.Blocker);
        Assert.Equal(attempt, cancelled.ActiveAttempt);
        Assert.NotEqual(Guid.Empty, blocker.Id.Value);

        var outcome = await outcomeWait;
        Assert.Equal(task.Id, outcome.Synapse.Task);
        Assert.Equal(worker.Id, outcome.Synapse.Worker);
        Assert.Equal(attempt, outcome.Synapse.Attempt);
        Assert.Equal(blocker.Id, outcome.Synapse.Blocker);

        var read = await brain.Client.Get<ITask>(task.Id.Name)
            .SendAsync(new ReadTaskOperation(attempt, Sequence: 0), cancellationToken);
        Assert.NotNull(read.Operation);
        Assert.Equal(TaskOperationPhase.Uncertain, read.Operation.Phase);
        Assert.Null(read.Operation.ResponsePayload);

        var still = await task.Reference.Read();
        Assert.Equal(TaskState.Waiting, still.State);
        Assert.IsType<OutcomeUncertain>(still.Blocker);
        Assert.NotEqual(TaskState.Cancelled, still.State);
        Assert.NotEqual(TaskState.Cancelling, still.State);
    }

    [Fact(DisplayName =
        "Cancel with Dispatched still dispatches worker cancel and keeps OutcomeUncertain sticky against late AttemptSucceeded")]
    public async Task CancelWithDispatchedKeepsOutcomeUncertainStickyAgainstLateSucceeded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var (worker, task, _) = await StartAsync(brain, "cancel-sticky", new TestGoal("cancel-sticky"));
        var running = await AcceptThenRunningAsync(task, cancellationToken);
        Assert.NotNull(running.ActiveAttempt);
        var attempt = running.ActiveAttempt.Value;
        var edge = new TaskOperationEdge(
            new NeuronId("provider", task.Id.Owner, "gmail"),
            "test.provider-request",
            RequestSchemaVersion: 1,
            "test.provider-response",
            ResponseSchemaVersion: 1);
        var request = new ProtectedPayloadReference(
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));

        var preparedWait = worker.Incoming.NextAsync<TaskOperationSnapshot>(cancellationToken);
        await brain.Client.SendAsync<IWorker>(
            worker.Id.Name,
            new PrepareOperationProbe(task.Id, attempt, Sequence: 0, edge, request),
            cancellationToken);
        Assert.Equal(TaskOperationPhase.Prepared, (await preparedWait).Synapse.Phase);

        var dispatchedWait = worker.Incoming.NextAsync<TaskOperationSnapshot>(cancellationToken);
        await brain.Client.SendAsync<IWorker>(
            worker.Id.Name,
            new TransitionOperationProbe(
                task.Id,
                attempt,
                Sequence: 0,
                TaskOperationPhase.Prepared,
                TaskOperationPhase.Dispatched,
                ResponsePayload: null),
            cancellationToken);
        Assert.Equal(TaskOperationPhase.Dispatched, (await dispatchedWait).Synapse.Phase);

        var outcomeWait = task.Outgoing.NextAsync<AttemptOutcomeUncertain>(cancellationToken);
        var cancelledFactWait = task.Incoming.NextAsync<AttemptCancelled>(cancellationToken);
        var uncertain = await task.Reference.Cancel(new CancelTask(CommandId.New(), running.Revision));

        Assert.Equal(TaskState.Waiting, uncertain.State);
        Assert.IsType<OutcomeUncertain>(uncertain.Blocker);
        Assert.Equal(attempt, uncertain.ActiveAttempt);
        _ = await outcomeWait;

        await brain.Client.SendAsync<IWorker>(
            worker.Id.Name,
            new LateAttemptSucceededProbe(task.Id, attempt, running.Revision),
            cancellationToken);

        var afterLateSuccess = await task.Reference.Read();
        Assert.Equal(TaskState.Waiting, afterLateSuccess.State);
        Assert.IsType<OutcomeUncertain>(afterLateSuccess.Blocker);
        Assert.Equal(attempt, afterLateSuccess.ActiveAttempt);
        Assert.Null(afterLateSuccess.Result);
        Assert.NotEqual(TaskState.Succeeded, afterLateSuccess.State);
        Assert.NotEqual(TaskState.Cancelled, afterLateSuccess.State);

        using var cancelObserved = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancelObserved.CancelAfter(TimeSpan.FromSeconds(5));
        var cancelledFact = await cancelledFactWait.WaitAsync(cancelObserved.Token);
        AssertAttempt(cancelledFact, task.Id, worker.Id, attempt, running.Revision);

        var still = await task.Reference.Read();
        Assert.Equal(TaskState.Waiting, still.State);
        Assert.IsType<OutcomeUncertain>(still.Blocker);
    }
}
