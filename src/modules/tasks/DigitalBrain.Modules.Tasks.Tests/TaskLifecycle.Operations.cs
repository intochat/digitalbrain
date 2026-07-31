using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tasks.Tests;

public sealed partial class TaskLifecycle
{
    [Fact(DisplayName = "operation history survives task restart and replays completed result")]
    public async Task OperationHistorySurvivesTaskRestartAndReplaysCompletedResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var (worker, task, started) = await StartAsync(brain, "ops-replay", new TestGoal("ops-replay"));
        var running = await AcceptThenRunningAsync(task, cancellationToken);
        Assert.NotNull(running.ActiveAttempt);
        var attempt = running.ActiveAttempt.Value;

        var edge = ExactEdge(task.Id.Owner);
        var originalRequest = new ProtectedPayloadReference(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            DateTimeOffset.Parse("2026-07-31T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var response = new ProtectedPayloadReference(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            DateTimeOffset.Parse("2026-07-31T13:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

        var preparedRequestWait = task.Incoming.NextAsync<PrepareTaskOperation>(cancellationToken);
        var preparedWait = task.Outgoing.NextAsync<TaskOperationSnapshot>(cancellationToken);
        await brain.Client.SendAsync<IWorker>(
            worker.Id.Name,
            new PrepareOperationProbe(task.Id, attempt, Sequence: 0, edge, originalRequest),
            cancellationToken);
        var preparedRequest = await preparedRequestWait.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        Assert.Equal(attempt, preparedRequest.Synapse.Attempt);
        Assert.Equal(0, preparedRequest.Synapse.Sequence);
        Assert.Equal(edge, preparedRequest.Synapse.Edge);
        var prepared = await preparedWait.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        Assert.Equal(TaskOperationPhase.Prepared, prepared.Synapse.Phase);
        Assert.Equal(edge, prepared.Synapse.Edge);
        Assert.Equal(originalRequest, prepared.Synapse.RequestPayload);
        Assert.Equal(0, prepared.Synapse.Sequence);
        Assert.Equal(attempt, prepared.Synapse.Attempt);
        Assert.Null(prepared.Synapse.ResponsePayload);

        var dispatchedWait = task.Outgoing.NextAsync<TaskOperationSnapshot>(cancellationToken);
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
        var dispatched = await dispatchedWait.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        Assert.Equal(TaskOperationPhase.Dispatched, dispatched.Synapse.Phase);
        Assert.Equal(originalRequest, dispatched.Synapse.RequestPayload);
        Assert.Null(dispatched.Synapse.ResponsePayload);

        var completedWait = task.Outgoing.NextAsync<TaskOperationSnapshot>(cancellationToken);
        await brain.Client.SendAsync<IWorker>(
            worker.Id.Name,
            new TransitionOperationProbe(
                task.Id,
                attempt,
                Sequence: 0,
                TaskOperationPhase.Dispatched,
                TaskOperationPhase.Completed,
                response),
            cancellationToken);
        var completed = await completedWait.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        Assert.Equal(TaskOperationPhase.Completed, completed.Synapse.Phase);
        Assert.Equal(originalRequest, completed.Synapse.RequestPayload);
        Assert.Equal(response, completed.Synapse.ResponsePayload);
        Assert.NotEqual(Guid.Empty, completed.Synapse.ResponsePayload!.Value.Id);

        await task.RestartHostAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(45), cancellationToken);

        var read = await brain.Client.Get<ITask>(task.Id.Name)
            .SendAsync(new ReadTaskOperation(attempt, Sequence: 0), cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        Assert.NotNull(read.Operation);
        Assert.Equal(TaskOperationPhase.Completed, read.Operation.Phase);
        Assert.Equal(originalRequest, read.Operation.RequestPayload);
        Assert.Equal(response, read.Operation.ResponsePayload);
        Assert.Equal(0, read.Operation.Sequence);
        Assert.Equal(edge, read.Operation.Edge);
    }

    [Fact(DisplayName = "dispatched operation becomes Task OutcomeUncertain without retry")]
    public async Task DispatchedOperationBecomesTaskOutcomeUncertainWithoutRetry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var (worker, task, started) = await StartAsync(brain, "ops-uncertain", new TestGoal("ops-uncertain"));
        var running = await AcceptThenRunningAsync(task, cancellationToken);
        Assert.NotNull(running.ActiveAttempt);
        var attempt = running.ActiveAttempt.Value;
        var edge = ExactEdge(task.Id.Owner);
        var request = new ProtectedPayloadReference(
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));

        var preparedWait = worker.Incoming.NextAsync<TaskOperationSnapshot>(cancellationToken);
        await brain.Client.SendAsync<IWorker>(
            worker.Id.Name,
            new PrepareOperationProbe(task.Id, attempt, Sequence: 0, edge, request),
            cancellationToken);
        var prepared = await preparedWait.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        Assert.Equal(TaskOperationPhase.Prepared, prepared.Synapse.Phase);

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
        var dispatched = await dispatchedWait.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        Assert.Equal(TaskOperationPhase.Dispatched, dispatched.Synapse.Phase);

        var uncertainWait = worker.Incoming.NextAsync<TaskOperationSnapshot>(cancellationToken);
        var outcomeWait = task.Outgoing.NextAsync<AttemptOutcomeUncertain>(cancellationToken);
        await brain.Client.SendAsync<IWorker>(
            worker.Id.Name,
            new TransitionOperationProbe(
                task.Id,
                attempt,
                Sequence: 0,
                TaskOperationPhase.Dispatched,
                TaskOperationPhase.Uncertain,
                ResponsePayload: null),
            cancellationToken);
        var uncertain = await uncertainWait.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        Assert.Equal(TaskOperationPhase.Uncertain, uncertain.Synapse.Phase);
        Assert.Null(uncertain.Synapse.ResponsePayload);

        var waiting = await WaitForStateAsync(task, TaskState.Waiting, cancellationToken);
        var blocker = Assert.IsType<OutcomeUncertain>(waiting.Blocker);
        Assert.NotEqual(Guid.Empty, blocker.Id.Value);
        Assert.Equal(attempt, waiting.ActiveAttempt);

        var outcome = await outcomeWait.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        Assert.Equal(task.Id, outcome.Synapse.Task);
        Assert.Equal(worker.Id, outcome.Synapse.Worker);
        Assert.Equal(attempt, outcome.Synapse.Attempt);
        Assert.Equal(running.Revision, outcome.Synapse.Revision);
        Assert.Equal(blocker.Id, outcome.Synapse.Blocker);
        Assert.NotEqual(Guid.Empty, outcome.Synapse.Blocker.Value);
    }

    private static TaskOperationEdge ExactEdge(OwnerId owner)
        => new(
            new NeuronId("provider", owner, "gmail"),
            "test.provider-request",
            RequestSchemaVersion: 1,
            "test.provider-response",
            ResponseSchemaVersion: 1);
}
