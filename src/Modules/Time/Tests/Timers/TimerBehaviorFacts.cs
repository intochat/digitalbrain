using DigitalBrain.Time.Timers.Signals;
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
        var result = new TaskCompletionSource<TimerTick>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var run = host.RunBehavior((brain, token) => TimerReport.RunAsync(brain, "behavior", fact =>
        {
            result.TrySetResult(fact);
            return Task.CompletedTask;
        }, token), ct);
        await run.WaitForSubscriptionAsync<TimerTick>(timer, ct);
        await using var otherTicks = await host.ObserveAsync<TimerTick>(other, ct);
        await other.Start(TimeSpan.Zero);
        await otherTicks.NextAsync(ct: ct);
        Assert.False(result.Task.IsCompleted);
        await timer.Start(TimeSpan.Zero);
        var fact = await result.Task.WaitAsync(TimeSpan.FromSeconds(90), ct);
        Assert.Equal("behavior", fact.TimerId);
        await run.DisposeAsync();
        Assert.True(run.Completion.IsCompleted);
    }
}
