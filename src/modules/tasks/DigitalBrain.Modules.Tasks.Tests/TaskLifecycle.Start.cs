using DigitalBrain.Abstractions;
using Xunit;

namespace DigitalBrain.Tasks.Tests;

public sealed partial class TaskLifecycle
{
    [Fact(DisplayName = "Start dispatches worker Accept, AttemptAccepted moves task to Running")]
    public async Task StartDispatchesAcceptAndAttemptAcceptedMovesTaskToRunning()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var goal = new TestGoal("ship");
        var (worker, task, started) = await StartAsync(brain, "start", goal);

        Assert.Equal(TaskState.Pending, started.State);
        Assert.Equal(0, started.Revision);
        Assert.Equal(worker.Id, started.Worker);
        Assert.Equal(goal, started.Goal);
        Assert.NotNull(started.ActiveAttempt);

        var accepted = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        AssertAttempt(accepted, task.Id, worker.Id, started.ActiveAttempt, started.Revision);

        var running = await WaitForStateAsync(task, TaskState.Running, cancellationToken);

        Assert.Equal(started.ActiveAttempt, running.ActiveAttempt);
        Assert.Equal(started.Revision, running.Revision);
        Assert.Null(running.Blocker);
        Assert.Null(running.Result);
        Assert.Null(running.Failure);
    }

    [Fact(DisplayName = "Start is idempotent for the same CommandId receipt")]
    public async Task StartIsIdempotentForTheSameCommandId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var worker = brain.Neuron<IWorker>("idempotent-worker");
        var task = brain.Neuron<ITask>("idempotent-task");
        var command = StartCommand(new TestGoal("idempotent"), worker.Id);

        var first = await task.Reference.Start(command);
        var running = await AcceptThenRunningAsync(task, cancellationToken);
        var repeated = await task.Reference.Start(command);

        AssertReceipt(first, repeated);
        Assert.Equal(TaskState.Pending, repeated.State);
        Assert.Equal(running.ActiveAttempt, repeated.ActiveAttempt);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => task.Reference.Start(StartCommand(
                new TestGoal("second-start"),
                worker.Id)));

        Assert.Equal(TaskState.Running, (await task.Reference.Read()).State);
    }

    [Fact(DisplayName = "Start pins task to behavior id, revision, contract version, case id, and opaque ProtectedPayloadReference")]
    public async Task StartPinsBehaviorActivationIdentity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var worker = brain.Neuron<IWorker>("pin-worker");
        var task = brain.Neuron<ITask>("pin-task");
        var activation = new BehaviorTaskActivation(
            new BehaviorId("sample"),
            new BehaviorRevisionId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            contractVersion: "1",
            caseId: "install",
            protectedPayload: new ProtectedPayloadReference(Guid.Parse("11111111-1111-1111-1111-111111111111")));

        var started = await task.Reference.Start(new StartTask(
            CommandId.New(),
            new TestGoal("pinned"),
            worker.Id,
            TaskFixtures.SingleAttempt,
            Activation: activation));

        Assert.Equal(TaskState.Pending, started.State);
        Assert.NotNull(started.ActiveAttempt);
        Assert.Equal(activation, started.Activation);
        Assert.Equal(activation.BehaviorId, started.Activation!.BehaviorId);
        Assert.Equal(activation.Revision, started.Activation.Revision);
        Assert.Equal(activation.ContractVersion, started.Activation.ContractVersion);
        Assert.Equal(activation.CaseId, started.Activation.CaseId);
        Assert.Equal(activation.ProtectedPayload, started.Activation.ProtectedPayload);

        var read = await task.Reference.Read();
        Assert.Equal(activation, read.Activation);
        Assert.Equal(started.ActiveAttempt, read.ActiveAttempt);
    }

    [Fact(DisplayName = "Start directed request/result is idempotent; recovery cannot create a second attempt")]
    public async Task StartDirectedRequestResultIsIdempotentAcrossRecovery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var worker = brain.Neuron<IWorker>("directed-worker");
        var task = brain.Neuron<ITask>("directed-task");
        var command = StartCommand(new TestGoal("directed"), worker.Id);

        var first = await task.Reference.Start(command);
        var recovered = await task.Reference.Start(command);

        AssertReceipt(first, recovered);
        Assert.Equal(first.ActiveAttempt, recovered.ActiveAttempt);
        Assert.Equal(1, recovered.AttemptCount);

        var secondCommand = StartCommand(new TestGoal("directed-again"), worker.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => task.Reference.Start(secondCommand));

        var read = await task.Reference.Read();
        Assert.Equal(first.ActiveAttempt, read.ActiveAttempt);
        Assert.Equal(1, read.AttemptCount);
    }
}
