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

    [Fact(DisplayName = "AttemptProgressed stages Continue via one-shot relay with exact cursor")]
    public async Task AttemptProgressedStagesContinueThroughOneShotRelay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var worker = brain.Neuron<IWorker>("relay-continue-worker");
        var task = brain.Neuron<ITask>("relay-continue-task");

        var started = await task.Reference.Start(new StartTask(
            CommandId.New(),
            new ProgressGoal("relay-continue"),
            worker.Id,
            TaskFixtures.SingleAttempt));

        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        var progressed = await task.Incoming.NextAsync<AttemptProgressed>(cancellationToken);
        Assert.Equal(started.ActiveAttempt, progressed.Synapse.Attempt);

        var envelopes = await WaitForAsync(
            () => task.Outgoing.ReadAsync<RelayWorkerContinue>(afterSequence: 0, cancellationToken),
            list => list.Count > 0,
            cancellationToken);
        var envelope = Assert.Single(envelopes);
        Assert.Equal(worker.Id, envelope.Synapse.Worker);
        Assert.Equal(task.Id, envelope.Synapse.Cursor.Task);
        Assert.Equal(worker.Id, envelope.Synapse.Cursor.Worker);
        Assert.Equal(started.ActiveAttempt, envelope.Synapse.Cursor.Attempt);
        Assert.Equal(started.Revision + 1, envelope.Synapse.Cursor.Revision);

        Assert.Empty(await task.Outgoing.ReadAsync<DispatchWorkerContinue>(afterSequence: 0, cancellationToken));

        var delivered = await WaitForAsync(
            () => worker.Incoming.ReadAsync<DispatchWorkerContinue>(afterSequence: 0, cancellationToken),
            list => list.Count > 0,
            cancellationToken);
        var observed = Assert.Single(delivered);
        Assert.Equal(WorkerDispatchRelay.GrainTypeName, observed.Caller.Type);
        Assert.Equal(envelope.Synapse.Cursor, observed.Synapse.Cursor);

        var running = await WaitForStateAsync(task, TaskState.Running, cancellationToken);
        Assert.Equal(started.ActiveAttempt, running.ActiveAttempt);
        Assert.Equal(started.Revision + 1, running.Revision);
        Assert.Null(running.Blocker);

        var responsive = await task.Reference.Read().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        Assert.Equal(TaskState.Running, responsive.State);
        Assert.Equal(worker.Id, responsive.Worker);
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

    [Fact(DisplayName =
        "after Task stages relay Accept, ownership transfer leaves a single relay envelope even after dispatch-reminder horizon")]
    public async Task AfterStagingOwnershipTransferDoesNotResendAcceptOnReminderHorizon()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var worker = brain.Neuron<IWorker>("relay-ownership-worker");
        var task = brain.Neuron<ITask>("relay-ownership-task");

        await task.Reference.Start(new StartTask(
            CommandId.New(),
            new TestGoal("relay-ownership"),
            worker.Id,
            TaskFixtures.SingleAttempt));
        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        _ = await WaitForStateAsync(task, TaskState.Running, cancellationToken);

        var first = await task.Outgoing
            .ReadAsync<RelayWorkerAccept>(afterSequence: 0, cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        Assert.Single(first);

        // Dispatch reminder period is 1 minute; PendingDispatch is already cleared after staging.
        await brain.Clock.AdvanceAsync(TimeSpan.FromMinutes(2), cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);

        var after = await task.Outgoing
            .ReadAsync<RelayWorkerAccept>(afterSequence: 0, cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        Assert.Single(after);
        Assert.Single(
            await worker.Incoming
                .ReadAsync<DispatchWorkerAccept>(afterSequence: 0, cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken));
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

    [Fact(DisplayName = "idempotent Cancel receipt does not stage a second Cancel dispatch")]
    public async Task IdempotentCancelDoesNotStageSecondCancelDispatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var worker = brain.Neuron<IWorker>("relay-cancel-idemp-worker");
        var task = brain.Neuron<ITask>("relay-cancel-idemp-task");

        await task.Reference.Start(new StartTask(
            CommandId.New(),
            new TestGoal("relay-cancel-idemp"),
            worker.Id,
            TaskFixtures.SingleAttempt));
        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        var running = await WaitForStateAsync(task, TaskState.Running, cancellationToken);

        var command = new CancelTask(CommandId.New(), running.Revision);
        var first = await task.Reference.Cancel(command);
        Assert.Equal(TaskState.Cancelling, first.State);

        var envelopes = await WaitForAsync(
            () => task.Outgoing.ReadAsync<RelayWorkerCancel>(afterSequence: 0, cancellationToken),
            list => list.Count > 0,
            cancellationToken);
        Assert.Single(envelopes);

        var repeated = await task.Reference.Cancel(command);
        Assert.Equal(first.State, repeated.State);
        Assert.Equal(first.Revision, repeated.Revision);

        await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        Assert.Single(
            await task.Outgoing
                .ReadAsync<RelayWorkerCancel>(afterSequence: 0, cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken));

        _ = await task.Incoming.NextAsync<AttemptCancelled>(cancellationToken);
        _ = await WaitForStateAsync(task, TaskState.Cancelled, cancellationToken);
    }

    [Fact(DisplayName = "relay refuses foreign-owner Worker target without staging Worker Accept")]
    public async Task RelayRefusesForeignOwnerWorkerWithoutStagingWorkerAccept()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var worker = brain.Neuron<IWorker>("relay-refuse-worker");
        var task = brain.Neuron<ITask>("relay-refuse-task");

        var started = await task.Reference.Start(new StartTask(
            CommandId.New(),
            new TestGoal("relay-refuse"),
            worker.Id,
            TaskFixtures.SingleAttempt));
        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        _ = await WaitForStateAsync(task, TaskState.Running, cancellationToken);

        var acceptsBefore = await worker.Incoming
            .ReadAsync<DispatchWorkerAccept>(afterSequence: 0, cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        Assert.Single(acceptsBefore);

        var foreignWorker = new NeuronId(
            NeuronId.GrainTypeNameOf(typeof(IWorker)),
            new OwnerId("foreign-relay-owner"),
            "foreign-worker");
        var relay = new NeuronId(
            WorkerDispatchRelay.GrainTypeName,
            task.Id.Owner,
            Guid.NewGuid().ToString("N"));
        await brain.Client
            .SendAsync(
                relay,
                new RelayWorkerAccept(
                    foreignWorker,
                    new AttemptRequest(
                        task.Id,
                        foreignWorker,
                        started.ActiveAttempt!.Value,
                        started.Revision,
                        new TestGoal("relay-refuse"))),
                cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
        var acceptsAfter = await worker.Incoming
            .ReadAsync<DispatchWorkerAccept>(afterSequence: 0, cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        Assert.Single(acceptsAfter);

        var stillRunning = await task.Reference.Read().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        Assert.Equal(TaskState.Running, stillRunning.State);
    }

    [Fact(DisplayName = "relay refuses embedded Worker/Task mismatch without staging Worker Accept")]
    public async Task RelayRefusesWorkerTaskMismatchWithoutStagingWorkerAccept()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var worker = brain.Neuron<IWorker>("relay-mismatch-worker");
        var otherWorker = brain.Neuron<IWorker>("relay-mismatch-other-worker");
        var task = brain.Neuron<ITask>("relay-mismatch-task");

        var started = await task.Reference.Start(new StartTask(
            CommandId.New(),
            new TestGoal("relay-mismatch"),
            worker.Id,
            TaskFixtures.SingleAttempt));
        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        _ = await WaitForStateAsync(task, TaskState.Running, cancellationToken);

        var acceptsBefore = await worker.Incoming
            .ReadAsync<DispatchWorkerAccept>(afterSequence: 0, cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        Assert.Single(acceptsBefore);

        var foreignTask = new NeuronId(
            NeuronId.GrainTypeNameOf(typeof(ITask)),
            new OwnerId("foreign-task-owner"),
            "foreign-task");
        var relay = new NeuronId(
            WorkerDispatchRelay.GrainTypeName,
            task.Id.Owner,
            Guid.NewGuid().ToString("N"));
        await brain.Client
            .SendAsync(
                relay,
                new RelayWorkerAccept(
                    worker.Id,
                    new AttemptRequest(
                        foreignTask,
                        otherWorker.Id,
                        started.ActiveAttempt!.Value,
                        started.Revision,
                        new TestGoal("relay-mismatch"))),
                cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
        Assert.Single(
            await worker.Incoming
                .ReadAsync<DispatchWorkerAccept>(afterSequence: 0, cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken));
        Assert.Empty(
            await otherWorker.Incoming
                .ReadAsync<DispatchWorkerAccept>(afterSequence: 0, cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken));
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
