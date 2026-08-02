using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Runtime;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class BehaviorWorkerAuthorityTests(BehaviorsFixture fixture)
{
    [Fact(
        Timeout = 60_000,
        DisplayName = "session and foreign caller cannot prepare or transition task operations")]
    public async Task SessionAndForeignCallerCannotPrepareOrTransition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var (worker, task, attempt) = await StartAcceptedBehaviorTaskAsync(brain, "authority-refuse", cancellationToken);
        var edge = ExactEdge(task.Id.Owner);
        var request = new ProtectedPayloadReference(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        // One-way session Fire (Synapse overload): TaskNeuron.Deliver refuses non-Worker
        // Prepare/Transition without a result reply, so RequestSynapse SendAsync would hang.
        await brain.Client.Get<ITask>(task.Id.Name)
            .SendAsync(
                (Synapse)new PrepareTaskOperation(attempt, Sequence: 0, edge, request),
                cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        await brain.Client.Get<ITask>(task.Id.Name)
            .SendAsync(
                (Synapse)new TransitionTaskOperation(
                    attempt,
                    Sequence: 0,
                    TaskOperationPhase.Prepared,
                    TaskOperationPhase.Dispatched,
                    ResponsePayload: null,
                    RedactedSummary: null),
                cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        // Allow refused deliveries to drain, then prove Task history stayed empty.
        await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);

        var access = new GrainBehaviorTaskOperationAccess(brain.Cluster.Client);
        var read = await access.ReadAsync(
            task.Id.Owner,
            task.Id,
            attempt,
            sequence: 0,
            cancellationToken);
        Assert.Null(read.Operation);

        var foreign = brain.Owner("foreign-authority-owner");
        await Assert.ThrowsAsync<NeuronAuthorizationException>(async () =>
            await foreign.Client
                .SendAsync(
                    task.Id,
                    (Synapse)new PrepareTaskOperation(attempt, Sequence: 0, edge, request),
                    cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken));

        _ = worker;
    }

    [Fact(
        Timeout = 60_000,
        DisplayName = "production Worker-staged prepare succeeds and Task journal Caller equals worker.Id")]
    public async Task ProductionWorkerStagedPrepareSucceedsWithWorkerCallerEvidence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var (worker, task, attempt) = await StartAcceptedBehaviorTaskAsync(brain, "authority-ok", cancellationToken);
        var edge = ExactEdge(task.Id.Owner);
        var request = new ProtectedPayloadReference(Guid.Parse("22222222-2222-2222-2222-222222222222"));

        var access = new GrainBehaviorTaskOperationAccess(brain.Cluster.Client);
        var prepared = await access.PrepareAsync(
            task.Id.Owner,
            task.Id,
            attempt,
            sequence: 0,
            edge,
            request,
            cancellationToken);

        Assert.Equal(TaskOperationPhase.Prepared, prepared.Phase);
        Assert.Equal(0, prepared.Sequence);
        Assert.Equal(attempt, prepared.Attempt);
        Assert.Equal(request, prepared.RequestPayload);

        // Evidence: Prepare was delivered with Worker as Caller (TaskNeuron.Deliver gate).
        var preparedRequests = await task.Incoming
            .ReadAsync<PrepareTaskOperation>(afterSequence: 0, cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        var workerStaged = Assert.Single(preparedRequests, observed => observed.Caller == worker.Id);
        Assert.Equal(0, workerStaged.Synapse.Sequence);
        Assert.Equal(edge, workerStaged.Synapse.Edge);

        var snapshots = await worker.Incoming
            .ReadAsync<TaskOperationSnapshot>(afterSequence: 0, cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        var reply = Assert.Single(
            snapshots,
            observed => observed.Caller == task.Id && observed.Synapse.Sequence == 0);
        Assert.Equal(TaskOperationPhase.Prepared, reply.Synapse.Phase);
    }

    [Fact(
        Timeout = 60_000,
        DisplayName = "worker, task, and attempt mismatches are refused before staging")]
    public async Task WorkerTaskAttemptMismatchIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var (_, task, attempt) = await StartAcceptedBehaviorTaskAsync(brain, "authority-mismatch", cancellationToken);
        var edge = ExactEdge(task.Id.Owner);
        var request = new ProtectedPayloadReference(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var access = new GrainBehaviorTaskOperationAccess(brain.Cluster.Client);

        var wrongAttempt = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await access.PrepareAsync(
                task.Id.Owner,
                task.Id,
                new AttemptId(Guid.Parse("44444444-4444-4444-4444-444444444444")),
                sequence: 0,
                edge,
                request,
                cancellationToken));
        Assert.Equal("attempt-mismatch", wrongAttempt.Message);

        var missingTask = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await access.PrepareAsync(
                task.Id.Owner,
                NeuronId.For<ITask>(task.Id.Owner, "missing-task"),
                attempt,
                sequence: 0,
                edge,
                request,
                cancellationToken));
        Assert.Equal("task-not-started", missingTask.Message);

        var ownerMismatch = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await access.PrepareAsync(
                new OwnerId("other-owner-for-auth"),
                task.Id,
                attempt,
                sequence: 0,
                edge,
                request,
                cancellationToken));
        Assert.Equal("owner-task-mismatch", ownerMismatch.Message);
        Assert.NotEqual("task-not-started", ownerMismatch.Message);
    }

    [Fact(
        Timeout = 90_000,
        DisplayName =
            "Task.Cancel racing Prepare through Worker authority completes without Task↔Worker lock inversion hang")]
    public async Task ConcurrentTaskCancelAndPrepareDoesNotHang()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var (worker, task, attempt) = await StartAcceptedBehaviorTaskAsync(brain, "authority-abba", cancellationToken);
        var edge = ExactEdge(task.Id.Owner);
        var request = new ProtectedPayloadReference(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var access = new GrainBehaviorTaskOperationAccess(brain.Cluster.Client);

        var preRace = await task.Reference.Read().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        Assert.Equal(TaskState.Running, preRace.State);
        Assert.Equal(attempt, preRace.ActiveAttempt);

        var prepare = access.PrepareAsync(
            task.Id.Owner,
            task.Id,
            attempt,
            sequence: 0,
            edge,
            request,
            cancellationToken).AsTask();
        var cancel = task.Reference.Cancel(new CancelTask(CommandId.New(), preRace.Revision));

        await Task.WhenAny(prepare, cancel).WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        await Task.WhenAll(prepare.ContinueWith(static _ => { }, TaskScheduler.Default), cancel)
            .WaitAsync(TimeSpan.FromSeconds(40), cancellationToken);

        Assert.True(cancel.IsCompletedSuccessfully);
        var cancelSnapshot = await cancel;
        Assert.Equal(TaskState.Cancelling, cancelSnapshot.State);

        if (prepare.IsFaulted)
        {
            var failure = prepare.Exception?.GetBaseException();
            var timeout = Assert.IsType<InvalidOperationException>(failure);
            Assert.Equal("operation-timeout", timeout.Message);
        }
        else
        {
            Assert.True(prepare.IsCompletedSuccessfully);
            var prepared = await prepare;
            Assert.Equal(TaskOperationPhase.Prepared, prepared.Phase);
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        TaskSnapshot postRace;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            postRace = await task.Reference.Read().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (postRace.State == TaskState.Cancelled)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }
        while (DateTime.UtcNow < deadline);

        Assert.Equal(TaskState.Cancelled, postRace.State);
        Assert.Equal(worker.Id, postRace.Worker);

        var secondRead = await task.Reference.Read().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        Assert.Equal(TaskState.Cancelled, secondRead.State);
        Assert.Equal(worker.Id, secondRead.Worker);
        _ = await worker.Incoming
            .ReadAsync<Synapse>(afterSequence: 0, cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
    }

    [Fact(
        Timeout = 60_000,
        DisplayName = "Task permanent phase failure yields bounded operation-timeout (NACK deferred this checkpoint)")]
    public async Task PermanentTaskPhaseFailureYieldsBoundedOperationTimeout()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var (_, task, attempt) = await StartAcceptedBehaviorTaskAsync(brain, "authority-timeout", cancellationToken);
        var edge = ExactEdge(task.Id.Owner);
        var request = new ProtectedPayloadReference(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var access = new GrainBehaviorTaskOperationAccess(brain.Cluster.Client);

        await access.PrepareAsync(
            task.Id.Owner,
            task.Id,
            attempt,
            sequence: 0,
            edge,
            request,
            cancellationToken);

        var started = DateTime.UtcNow;
        var timeout = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await access.TransitionAsync(
                task.Id.Owner,
                task.Id,
                attempt,
                sequence: 0,
                expectedPhase: TaskOperationPhase.Completed,
                phase: TaskOperationPhase.Dispatched,
                responsePayload: null,
                redactedSummary: null,
                cancellationToken));
        Assert.Equal("operation-timeout", timeout.Message);
        Assert.True(
            DateTime.UtcNow - started < TimeSpan.FromSeconds(35),
            "operation-timeout must resolve under the access adapter wait bound");
    }

    private static async Task<(
        TestNeuron<IWorker> Worker,
        TestNeuron<ITask> Task,
        AttemptId Attempt)> StartAcceptedBehaviorTaskAsync(
        TestBrain brain,
        string name,
        CancellationToken cancellationToken)
    {
        var worker = brain.Neuron<IWorker>($"{name}-worker");
        var task = brain.Neuron<ITask>($"{name}-task");
        var activation = new BehaviorTaskActivation(
            new BehaviorId("com.digitalbrain.sample"),
            new BehaviorRevisionId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            contractVersion: "1",
            caseId: "authority",
            protectedPayload: new ProtectedPayloadReference(Guid.Parse("55555555-5555-5555-5555-555555555555")),
            triggerTypeName: "SampleTrigger",
            capabilities: []);
        var goal = new BehaviorActivationGoal(
            activation.BehaviorId,
            activation.Revision,
            activation.ContractVersion,
            activation.CaseId,
            activation.ProtectedPayload,
            activation.TriggerTypeName,
            activation.Capabilities);

        await task.Reference.Start(new StartTask(
            CommandId.New(),
            goal,
            worker.Id,
            new TaskPolicy(1, TimeSpan.Zero, null),
            Activation: activation))
            .WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);

        // Poll authoritative Read rather than racing a post-Start journal watch for Accept.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        TaskSnapshot snapshot;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            snapshot = await task.Reference.Read().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (snapshot.State == TaskState.Running && snapshot.ActiveAttempt is not null)
            {
                return (worker, task, snapshot.ActiveAttempt.Value);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException(
            $"Task stayed in {snapshot.State} instead of Running after Accept (worker={worker.Id}).");
    }

    private static TaskOperationEdge ExactEdge(OwnerId owner)
        => new(
            new NeuronId("provider", owner, "gmail"),
            "test.provider-request",
            RequestSchemaVersion: 1,
            "test.provider-response",
            ResponseSchemaVersion: 1);
}
