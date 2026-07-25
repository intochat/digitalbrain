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
        var (worker, task, started) = await StartAsync(
            brain,
            "cancel",
            new TestGoal("cancel-me"));
        var running = await AcceptThenRunningAsync(task, cancellationToken);

        var cancel = new CancelTask(CommandId.New(), running.Revision);
        var cancelling = await task.Reference.Cancel(cancel);

        Assert.Equal(TaskState.Cancelling, cancelling.State);
        Assert.Equal(running.Revision, cancelling.Revision);
        Assert.Equal(running.ActiveAttempt, cancelling.ActiveAttempt);
        AssertReceipt(cancelling, await task.Reference.Cancel(cancel));

        var cancelledFact = await task.Incoming.NextAsync<AttemptCancelled>(
            cancellationToken);
        AssertAttempt(
            cancelledFact,
            task.Id,
            worker.Id,
            running.ActiveAttempt,
            running.Revision);

        var cancelled = await WaitForStateAsync(
            task,
            TaskState.Cancelled,
            cancellationToken);

        Assert.Null(cancelled.ActiveAttempt);
        Assert.Null(cancelled.Blocker);
        Assert.Equal(started.Goal, cancelled.Goal);
    }
}
