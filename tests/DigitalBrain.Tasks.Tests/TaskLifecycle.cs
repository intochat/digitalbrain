using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tasks.Tests;

public sealed class TaskLifecycle(TasksFixture fixture)
{
    private static readonly TaskPolicy DefaultPolicy = new(
        MaximumAttempts: 1,
        RetryDelay: TimeSpan.FromSeconds(1),
        Deadline: null);

    [Fact(DisplayName = "Start dispatches worker Accept, AttemptAccepted moves task to Running")]
    public async Task StartDispatchesAcceptAndAttemptAcceptedMovesTaskToRunning()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var worker = test.Neuron<IWorker>("accepting");
        var task = test.Neuron<ITask>("running");
        var goal = new TestGoal("ship");
        var command = new StartTask(
            CommandId.New(),
            goal,
            worker.Id,
            DefaultPolicy);

        var started = await task.Reference.Start(command);
        Assert.Equal(TaskState.Pending, started.State);
        Assert.Equal(0, started.Revision);
        Assert.Equal(worker.Id, started.Worker);
        Assert.Equal(goal, started.Goal);
        Assert.NotNull(started.ActiveAttempt);

        var accepted = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        Assert.Equal(task.Id, accepted.Synapse.Task);
        Assert.Equal(worker.Id, accepted.Synapse.Worker);
        Assert.Equal(started.ActiveAttempt, accepted.Synapse.Attempt);
        Assert.Equal(0, accepted.Synapse.Revision);
        Assert.Equal(worker.Id, accepted.Caller);

        var running = await WaitForStateAsync(
            task,
            TaskState.Running,
            cancellationToken);

        Assert.Equal(TaskState.Running, running.State);
        Assert.Equal(0, running.Revision);
        Assert.Equal(started.ActiveAttempt, running.ActiveAttempt);
        Assert.Null(running.Blocker);
        Assert.Null(running.Result);
        Assert.Null(running.Failure);
    }

    [Fact(DisplayName = "Start is idempotent for the same CommandId receipt")]
    public async Task StartIsIdempotentForTheSameCommandId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var worker = test.Neuron<IWorker>("idempotent-worker");
        var task = test.Neuron<ITask>("idempotent-task");
        var command = new StartTask(
            CommandId.New(),
            new TestGoal("idempotent"),
            worker.Id,
            DefaultPolicy);

        var first = await task.Reference.Start(command);
        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        var running = await WaitForStateAsync(
            task,
            TaskState.Running,
            cancellationToken);

        var repeated = await task.Reference.Start(command);

        AssertReceipt(first, repeated);
        Assert.Equal(TaskState.Pending, repeated.State);
        Assert.Equal(running.ActiveAttempt, repeated.ActiveAttempt);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => task.Reference.Start(new StartTask(
                CommandId.New(),
                new TestGoal("second-start"),
                worker.Id,
                DefaultPolicy)));

        Assert.Equal(
            TaskState.Running,
            (await task.Reference.Read()).State);
    }

    [Fact(DisplayName = "Cancel moves Cancelling then AttemptCancelled to Cancelled")]
    public async Task CancelMovesThroughCancellingToCancelled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var worker = test.Neuron<IWorker>("cancel-worker");
        var task = test.Neuron<ITask>("cancel-task");
        var start = new StartTask(
            CommandId.New(),
            new TestGoal("cancel-me"),
            worker.Id,
            DefaultPolicy);

        var started = await task.Reference.Start(start);
        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        var running = await WaitForStateAsync(
            task,
            TaskState.Running,
            cancellationToken);

        var cancel = new CancelTask(CommandId.New(), running.Revision);
        var cancelling = await task.Reference.Cancel(cancel);

        Assert.Equal(TaskState.Cancelling, cancelling.State);
        Assert.Equal(running.Revision, cancelling.Revision);
        Assert.Equal(running.ActiveAttempt, cancelling.ActiveAttempt);
        AssertReceipt(cancelling, await task.Reference.Cancel(cancel));

        var cancelledFact = await task.Incoming.NextAsync<AttemptCancelled>(
            cancellationToken);
        Assert.Equal(task.Id, cancelledFact.Synapse.Task);
        Assert.Equal(worker.Id, cancelledFact.Synapse.Worker);
        Assert.Equal(running.ActiveAttempt, cancelledFact.Synapse.Attempt);
        Assert.Equal(running.Revision, cancelledFact.Synapse.Revision);
        Assert.Equal(worker.Id, cancelledFact.Caller);

        var cancelled = await WaitForStateAsync(
            task,
            TaskState.Cancelled,
            cancellationToken);

        Assert.Equal(TaskState.Cancelled, cancelled.State);
        Assert.Null(cancelled.ActiveAttempt);
        Assert.Null(cancelled.Blocker);
        Assert.Equal(started.Goal, cancelled.Goal);
    }

    [Fact(DisplayName = "Stale revision attempt facts are ignored")]
    public async Task StaleRevisionAttemptFactsAreIgnored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var worker = test.Neuron<IWorker>("stale-worker");
        var task = test.Neuron<ITask>("stale-task");
        var started = await task.Reference.Start(new StartTask(
            CommandId.New(),
            new StaleProbeGoal("stale"),
            worker.Id,
            DefaultPolicy));

        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        var running = await WaitForStateAsync(
            task,
            TaskState.Running,
            cancellationToken);

        var stale = await task.Incoming.NextAsync<AttemptSucceeded>(cancellationToken);
        Assert.Equal(started.ActiveAttempt, stale.Synapse.Attempt);
        Assert.Equal(running.Revision + 1, stale.Synapse.Revision);

        var snapshot = await task.Reference.Read();
        Assert.Equal(TaskState.Running, snapshot.State);
        Assert.Equal(running.Revision, snapshot.Revision);
        Assert.Equal(running.ActiveAttempt, snapshot.ActiveAttempt);
        Assert.Null(snapshot.Result);
        Assert.Null(snapshot.Failure);
        Assert.Empty(snapshot.Evidence);
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

    private static void AssertReceipt(TaskSnapshot expected, TaskSnapshot actual)
    {
        Assert.Equal(expected.Goal, actual.Goal);
        Assert.Equal(expected.Worker, actual.Worker);
        Assert.Equal(expected.Policy, actual.Policy);
        Assert.Equal(expected.State, actual.State);
        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.ActiveAttempt, actual.ActiveAttempt);
        Assert.Equal(expected.Blocker, actual.Blocker);
        Assert.Equal(expected.Result, actual.Result);
        Assert.Equal(expected.Failure, actual.Failure);
        Assert.Equal(expected.Evidence, actual.Evidence);
        Assert.Equal(expected.RetryOf, actual.RetryOf);
    }
}
