using DigitalBrain.Abstractions;
using Xunit;

namespace DigitalBrain.Tasks.Tests;

public sealed partial class TaskLifecycle
{
    [Fact(DisplayName = "Matching AttemptSucceeded moves task to Succeeded with result and evidence")]
    public async Task MatchingAttemptSucceededMovesTaskToSucceededWithResultAndEvidence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var goal = new SuccessGoal("ship-it");
        var (worker, task, started) = await StartAsync(brain, "success", goal);

        var accepted = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        AssertAttempt(
            accepted,
            task.Id,
            worker.Id,
            started.ActiveAttempt,
            started.Revision);

        var succeededFact = await task.Incoming.NextAsync<AttemptSucceeded>(
            cancellationToken);
        AssertAttempt(
            succeededFact,
            task.Id,
            worker.Id,
            started.ActiveAttempt,
            started.Revision);
        Assert.Equal(TaskFixtures.Done, succeededFact.Synapse.Result);
        Assert.NotEmpty(succeededFact.Synapse.Evidence);

        var succeeded = await WaitForStateAsync(
            task,
            TaskState.Succeeded,
            cancellationToken);

        Assert.Null(succeeded.ActiveAttempt);
        Assert.Null(succeeded.Blocker);
        Assert.Null(succeeded.Failure);
        Assert.Equal(goal, succeeded.Goal);
        Assert.Equal(worker.Id, succeeded.Worker);
        Assert.Equal(succeededFact.Synapse.Result, succeeded.Result);
        Assert.Equal(succeededFact.Synapse.Evidence, succeeded.Evidence);
    }

    [Fact(DisplayName = "Stale revision attempt facts are ignored")]
    public async Task StaleRevisionAttemptFactsAreIgnored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var (_, task, started) = await StartAsync(
            brain,
            "stale",
            new StaleProbeGoal("stale"));
        var running = await AcceptThenRunningAsync(task, cancellationToken);

        var stale = await task.Incoming.NextAsync<AttemptSucceeded>(cancellationToken);
        Assert.Equal(started.ActiveAttempt, stale.Synapse.Attempt);
        Assert.Equal(running.Revision + 1, stale.Synapse.Revision);
        Assert.Equal(TaskFixtures.StaleSuccess, stale.Synapse.Result);

        var snapshot = await task.Reference.Read();
        Assert.Equal(TaskState.Running, snapshot.State);
        Assert.Equal(running.Revision, snapshot.Revision);
        Assert.Equal(running.ActiveAttempt, snapshot.ActiveAttempt);
        Assert.Null(snapshot.Result);
        Assert.Null(snapshot.Failure);
        Assert.Empty(snapshot.Evidence);
    }

    [Fact(DisplayName = "Retryable AttemptFailed schedules retry reminder then new Accept")]
    public async Task RetryableAttemptFailedSchedulesRetryThenNewAccept()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var (worker, task, started) = await StartAsync(
            brain,
            "retry",
            new RetryableFailureGoal("retry-me"),
            TaskFixtures.TwoAttempts);

        var firstAccepted = await task.Incoming.NextAsync<AttemptAccepted>(
            cancellationToken);
        Assert.Equal(started.ActiveAttempt, firstAccepted.Synapse.Attempt);
        Assert.Equal(started.Revision, firstAccepted.Synapse.Revision);

        var firstRunning = await WaitForStateAsync(
            task,
            TaskState.Running,
            cancellationToken);
        Assert.Equal(started.ActiveAttempt, firstRunning.ActiveAttempt);
        Assert.Equal(started.Revision, firstRunning.Revision);

        var failed = await task.Incoming.NextAsync<AttemptFailed>(cancellationToken);
        Assert.Equal(started.ActiveAttempt, failed.Synapse.Attempt);
        Assert.Equal(started.Revision, failed.Synapse.Revision);
        Assert.True(failed.Synapse.Retryable);
        Assert.Equal(TaskFixtures.Retryable, failed.Synapse.Failure);

        var waiting = await WaitForStateAsync(
            task,
            TaskState.Waiting,
            cancellationToken);
        Assert.IsType<RetryScheduled>(waiting.Blocker);
        Assert.Null(waiting.ActiveAttempt);
        Assert.Equal(TaskFixtures.Retryable, waiting.Failure);
        Assert.Equal(started.Revision, waiting.Revision);

        await brain.Clock.AdvanceAsync(TaskFixtures.RetryDelay, cancellationToken);

        var secondAccepted = await task.Incoming.NextAsync<AttemptAccepted>(
            cancellationToken);
        Assert.NotEqual(started.ActiveAttempt, secondAccepted.Synapse.Attempt);
        Assert.Equal(started.Revision + 1, secondAccepted.Synapse.Revision);
        Assert.Equal(worker.Id, secondAccepted.Synapse.Worker);

        var secondRunning = await WaitForStateAsync(
            task,
            TaskState.Running,
            cancellationToken);
        Assert.Equal(secondAccepted.Synapse.Attempt, secondRunning.ActiveAttempt);
        Assert.Equal(secondAccepted.Synapse.Revision, secondRunning.Revision);
        Assert.Null(secondRunning.Blocker);
        Assert.NotEqual(started.ActiveAttempt, secondRunning.ActiveAttempt);
    }
}
