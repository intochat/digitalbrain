using DigitalBrain.Time;
using Orleans;
using Orleans.Runtime;
using Xunit;
namespace DigitalBrain.Tests;

public sealed class TimerFacts
{
    [Fact]
    public async Task ScheduleAndStopReturnCommittedStateAndValidateTransitions()
    {
        await using var host = await TimerTestSupport.StartAsync(TestContext.Current.CancellationToken);
        var timer = host.Brain.Get<ITimer>("tea");
        Assert.Equal(TimerStatus.Unscheduled, (await timer.Read()).Status);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => timer.Schedule(0, "tea"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => timer.Schedule(-1, "tea"));
        await Assert.ThrowsAsync<ArgumentException>(() => timer.Schedule(60, " "));
        await Assert.ThrowsAsync<InvalidOperationException>(() => timer.Schedule(60, "tea", 2));
        var scheduled = await timer.Schedule(60, "tea", 0);
        Assert.Equal(TimerStatus.Scheduled, scheduled.Status);
        Assert.Equal(1, scheduled.Generation);
        Assert.Equal(scheduled, await timer.Read());
        await Assert.ThrowsAsync<InvalidOperationException>(() => timer.Schedule(60, "coffee"));
        Assert.Equal(scheduled, await timer.Read());
        var stopped = await timer.Stop();
        Assert.Equal(TimerStatus.Cancelled, stopped.Status);
        Assert.Equal(stopped, await timer.Stop());
        Assert.Equal(2, (await timer.Schedule(90, "coffee", 1)).Generation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefusedWriteRecoversCommittedState(bool refuseReload)
    {
        var faults = new StorageFaults();
        await using var host = await TimerTestSupport.StartAsync(TestContext.Current.CancellationToken, faults: faults);
        var timer = host.Brain.Get<ITimer>("refuse");
        var scheduled = await timer.Schedule(600, "tea");
        faults.FailNextWrite(timer.GetGrainId());
        if (refuseReload) { faults.FailNextRead(timer.GetGrainId()); }
        var error = await Assert.ThrowsAsync<OrleansException>(timer.Stop);
        Assert.IsType<IOException>(error.InnerException);
        Assert.Equal(scheduled, await timer.Read());
    }

    [Fact]
    public async Task FreshHostReadsTheCommittedTimer()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), "brain-time-" + Guid.NewGuid().ToString("N"));
        try
        {
            TimerSnapshot scheduled;
            await using (var first = await TimerTestSupport.StartAsync(ct, directory))
            {
                scheduled = await first.Brain.Get<ITimer>("saved").Schedule(600, "tea");
            }
            await using var second = await TimerTestSupport.StartAsync(ct, directory);
            Assert.Equal(scheduled, await second.Brain.Get<ITimer>("saved").Read());
        }
        finally { Directory.Delete(directory, true); }
    }
}

