using DigitalBrain.Testing.Unit;
using DigitalBrain.Testing;
using DigitalBrain.Time;
using DigitalBrain.Time.Timers.Signals;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TimerBehaviorFacts
{
    [Fact]
    public async Task BehaviorSubscribesBeforeTheTriggerAndOnlyReportsItsTimer()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await DigitalBrainSimulation.StartAsync(new()
        {
            Modules = [new TimeModule()],
            UseReminders = true,
        }, ct);
        var timer = brain.Get<ITimer>("behavior");
        var other = brain.Get<ITimer>("other");
        var result = new TaskCompletionSource<TimerTick>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var run = brain.RunBehavior((handle, token) => TimerReport.RunAsync(handle, "behavior", fact =>
        {
            result.TrySetResult(fact);
            return Task.CompletedTask;
        }, token), ct);
        await run.WaitForSubscriptionAsync<TimerTick>(timer, ct);
        await using var otherTicks = await brain.Observe<TimerTick>(other, ct);
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
