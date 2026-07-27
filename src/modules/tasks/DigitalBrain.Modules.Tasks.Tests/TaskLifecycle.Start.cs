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
        AssertAttempt(
            accepted,
            task.Id,
            worker.Id,
            started.ActiveAttempt,
            started.Revision);

        var running = await WaitForStateAsync(
            task,
            TaskState.Running,
            cancellationToken);

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

        Assert.Equal(
            TaskState.Running,
            (await task.Reference.Read()).State);
    }
}
