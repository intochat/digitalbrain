using DigitalBrain.Time;
using Xunit;
namespace DigitalBrain.Tests;

public sealed class TimerBehaviorFacts
{
    [Fact]
    public async Task BehaviorSubscribesBeforeTheTriggerAndOnlyReportsItsTimer()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TimerTestSupport.StartAsync(ct);
        var timer = host.Brain.Get<ITimer>("behavior");
        var other = host.Brain.Get<ITimer>("other");
        var result = new TaskCompletionSource<TimerElapsed>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var run = host.RunBehavior((brain, token) => TimerReport.RunAsync(brain, "behavior", fact =>
        {
            result.TrySetResult(fact);
            return Task.CompletedTask;
        }, token), ct);
        await run.WaitForSubscriptionAsync<TimerElapsed>(timer, ct);
        await other.Schedule(1, "other");
        await TestWait.UntilAsync(_ => other.Read(), value => value.Status == TimerStatus.Elapsed, TimeSpan.FromSeconds(90), ct);
        Assert.False(result.Task.IsCompleted);
        var scheduled = await timer.Schedule(1, "tea");
        var fact = await result.Task.WaitAsync(TimeSpan.FromSeconds(90), ct);
        Assert.Equal("behavior", fact.TimerId);
        Assert.Equal(scheduled.Generation, fact.Generation);
        await run.DisposeAsync();
        Assert.True(run.Completion.IsCompleted);
    }
}
