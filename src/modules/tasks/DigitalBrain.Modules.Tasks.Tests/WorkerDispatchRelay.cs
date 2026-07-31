using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tasks.Tests;

public sealed class WorkerDispatchRelayTests(TasksFixture fixture)
{
    [Fact(DisplayName = "Start stages Accept via one-shot relay, not direct Task→Worker dispatch")]
    public async Task StartStagesAcceptThroughOneShotRelayNotWorker()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var goal = new TestGoal("relay-accept");
        var worker = brain.Neuron<IWorker>("relay-accept-worker");
        var task = brain.Neuron<ITask>("relay-accept-task");

        var started = await task.Reference.Start(new StartTask(
            CommandId.New(),
            goal,
            worker.Id,
            TaskFixtures.SingleAttempt));

        var envelopes = await WaitForAsync(
            () => task.Outgoing.ReadAsync<RelayWorkerAccept>(afterSequence: 0, cancellationToken),
            list => list.Count > 0,
            cancellationToken);
        var envelope = Assert.Single(envelopes);
        Assert.Equal(worker.Id, envelope.Synapse.Worker);
        Assert.Equal(task.Id, envelope.Synapse.Request.Task);
        Assert.Equal(worker.Id, envelope.Synapse.Request.Worker);
        Assert.Equal(started.ActiveAttempt, envelope.Synapse.Request.Attempt);
        Assert.Equal(goal, envelope.Synapse.Request.Goal);

        Assert.Empty(await task.Outgoing.ReadAsync<DispatchWorkerAccept>(afterSequence: 0, cancellationToken));
        Assert.Empty(await task.Outgoing.ReadAsync<DispatchWorkerContinue>(afterSequence: 0, cancellationToken));
        Assert.Empty(await task.Outgoing.ReadAsync<DispatchWorkerCancel>(afterSequence: 0, cancellationToken));
        Assert.Empty(await task.Outgoing.ReadAsync<RelayWorkerContinue>(afterSequence: 0, cancellationToken));
        Assert.Empty(await task.Outgoing.ReadAsync<RelayWorkerCancel>(afterSequence: 0, cancellationToken));

        var accepted = await WaitForAsync(
            () => worker.Incoming.ReadAsync<DispatchWorkerAccept>(afterSequence: 0, cancellationToken),
            list => list.Count > 0,
            cancellationToken);
        var delivered = Assert.Single(accepted);
        Assert.Equal(WorkerDispatchRelay.GrainTypeName, delivered.Caller.Type);
        Assert.Equal(worker.Id, delivered.Synapse.Request.Worker);
        Assert.Equal(envelope.Synapse.Request, delivered.Synapse.Request);

        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        var running = await WaitForStateAsync(task, TaskState.Running, cancellationToken);
        Assert.Equal(started.ActiveAttempt, running.ActiveAttempt);
    }

    [Fact(DisplayName = "Cancel stages Cancel via one-shot relay with exact cursor payload")]
    public async Task CancelStagesThroughOneShotRelayWithExactCursor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var worker = brain.Neuron<IWorker>("relay-cancel-worker");
        var task = brain.Neuron<ITask>("relay-cancel-task");

        var started = await task.Reference.Start(new StartTask(
            CommandId.New(),
            new TestGoal("relay-cancel"),
            worker.Id,
            TaskFixtures.SingleAttempt));
        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        var running = await WaitForStateAsync(task, TaskState.Running, cancellationToken);

        var cancelled = await task.Reference.Cancel(new CancelTask(CommandId.New(), running.Revision));
        Assert.Equal(TaskState.Cancelling, cancelled.State);

        var envelopes = await WaitForAsync(
            () => task.Outgoing.ReadAsync<RelayWorkerCancel>(afterSequence: 0, cancellationToken),
            list => list.Count > 0,
            cancellationToken);
        var cancelEnvelope = Assert.Single(envelopes);
        Assert.Equal(worker.Id, cancelEnvelope.Synapse.Worker);
        Assert.Equal(task.Id, cancelEnvelope.Synapse.Cursor.Task);
        Assert.Equal(worker.Id, cancelEnvelope.Synapse.Cursor.Worker);
        Assert.Equal(started.ActiveAttempt, cancelEnvelope.Synapse.Cursor.Attempt);

        var delivered = await WaitForAsync(
            () => worker.Incoming.ReadAsync<DispatchWorkerCancel>(afterSequence: 0, cancellationToken),
            list => list.Count > 0,
            cancellationToken);
        var observed = Assert.Single(delivered);
        Assert.Equal(WorkerDispatchRelay.GrainTypeName, observed.Caller.Type);
        Assert.Equal(cancelEnvelope.Synapse.Cursor, observed.Synapse.Cursor);

        _ = await task.Incoming.NextAsync<AttemptCancelled>(cancellationToken);
        var terminal = await WaitForStateAsync(task, TaskState.Cancelled, cancellationToken);
        Assert.Equal(TaskState.Cancelled, terminal.State);
    }

    [Fact(DisplayName = "idempotent Start receipt does not stage a second Accept dispatch")]
    public async Task IdempotentStartDoesNotStageSecondAcceptDispatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var worker = brain.Neuron<IWorker>("relay-idempotent-worker");
        var task = brain.Neuron<ITask>("relay-idempotent-task");
        var command = new StartTask(
            CommandId.New(),
            new TestGoal("relay-idempotent"),
            worker.Id,
            TaskFixtures.SingleAttempt);

        var first = await task.Reference.Start(command);
        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        _ = await WaitForStateAsync(task, TaskState.Running, cancellationToken);

        var firstEnvelopes = await task.Outgoing
            .ReadAsync<RelayWorkerAccept>(afterSequence: 0, cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        Assert.Single(firstEnvelopes);

        var repeated = await task.Reference.Start(command);
        Assert.Equal(first.ActiveAttempt, repeated.ActiveAttempt);
        Assert.Equal(TaskState.Pending, repeated.State);

        await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        var afterRepeat = await task.Outgoing
            .ReadAsync<RelayWorkerAccept>(afterSequence: 0, cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        Assert.Single(afterRepeat);

        var accepts = await WaitForAsync(
            () => worker.Incoming.ReadAsync<DispatchWorkerAccept>(afterSequence: 0, cancellationToken),
            list => list.Count > 0,
            cancellationToken);
        Assert.Single(accepts);
    }

    [Fact(DisplayName = "internal worker-dispatch vocabulary is not exported from Tasks.Contracts")]
    public void InternalWorkerDispatchVocabularyIsNotExported()
    {
        var contracts = typeof(ITask).Assembly;
        Assert.DoesNotContain(
            contracts.GetExportedTypes(),
            type => type.Name is "RelayWorkerAccept"
                or "RelayWorkerContinue"
                or "RelayWorkerCancel"
                or "DispatchWorkerAccept"
                or "DispatchWorkerContinue"
                or "DispatchWorkerCancel"
                or "WorkerDispatchRelay"
                || type.Name.Contains("DispatchWorker", StringComparison.Ordinal)
                || type.Name.Contains("RelayWorker", StringComparison.Ordinal));

        Assert.NotNull(typeof(RelayWorkerAccept));
        Assert.NotNull(typeof(DispatchWorkerAccept));
        Assert.Equal("tasks.worker-dispatch-relay", WorkerDispatchRelay.GrainTypeName);
    }

    private static async Task<TaskSnapshot> WaitForStateAsync(
        TestNeuron<ITask> task,
        TaskState expected,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await task.Reference.Read();
            if (snapshot.State == expected)
            {
                return snapshot;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }

        var final = await task.Reference.Read();
        throw new TimeoutException(
            $"Task '{task.Id}' stayed in {final.State} instead of becoming {expected}.");
    }

    private static async Task<T> WaitForAsync<T>(
        Func<Task<T>> read,
        Func<T, bool> ready,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = await read().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (ready(value))
            {
                return value;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }

        var final = await read().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        if (ready(final))
        {
            return final;
        }

        throw new TimeoutException("Timed out waiting for journal condition.");
    }
}
