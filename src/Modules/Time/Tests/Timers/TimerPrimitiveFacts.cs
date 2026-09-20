using DigitalBrain.Testing.Unit;
using DigitalBrain.Testing;
using DigitalBrain.Time;
using DigitalBrain.Time.Timers.Signals;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TimerPrimitiveFacts
{
    [Fact]
    public async Task RealOneShotPublishesATick()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.StartAsync(new()
        {
            Modules = [new DigitalBrain.Core.ModuleDefinition(typeof(TimeModule))],
            UseReminders = true,
        }, ct);
        var timer = brain.Get<ITimer>("real");
        await using var ticks = await brain.Observe<TimerTick>(timer, ct);
        var before = DateTimeOffset.UtcNow;
        await timer.Start(TimeSpan.Zero);
        var tick = await ticks.NextAsync(ct: ct);
        Assert.Equal("real", tick.TimerId);
        Assert.InRange(tick.ObservedAt, before, DateTimeOffset.UtcNow);
        await timer.Stop();
        await timer.Stop();
    }
}
