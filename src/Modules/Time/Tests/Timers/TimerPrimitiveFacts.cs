using DigitalBrain.Time.Timers.Signals;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TimerPrimitiveFacts
{
    [Fact]
    public async Task RealOneShotPublishesATick()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TimerTestSupport.StartAsync(ct);
        var timer = host.Brain.Get<ITimer>("real");
        await using var ticks = await host.ObserveAsync<TimerTick>(timer, ct);
        var before = DateTimeOffset.UtcNow;
        await timer.Start(TimeSpan.Zero);
        var tick = await ticks.NextAsync(ct: ct);
        Assert.Equal("real", tick.TimerId);
        Assert.InRange(tick.ObservedAt, before, DateTimeOffset.UtcNow);
        await timer.Stop();
        await timer.Stop();
    }
}
