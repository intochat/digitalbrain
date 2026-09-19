using DigitalBrain.Time;
using Orleans;
using Orleans.Runtime;
using Xunit;
namespace DigitalBrain.Tests;

public sealed class TimerDueFacts
{
    [Fact]
    public async Task EarlyStaleAndRepeatedCallbacksDoNotPublish()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new ManualClock();
        await using var host = await TimerTestSupport.StartAsync(ct, time: time);
        var timer = host.Brain.Get<ITimerTestDriver>("controlled");
        await using var probe = await host.ObserveAsync<TimerElapsed>(timer, ct);
        var first = await timer.Schedule(600, "first");
        await timer.InvokeDue(first.Generation);
        Assert.Equal(first, await timer.Read());
        await timer.Stop();
        var next = await timer.Schedule(600, "next");
        time.Advance(TimeSpan.FromSeconds(661));
        await timer.InvokeDue(first.Generation);
        Assert.Equal(next, await timer.Read());
        await timer.InvokeDue(next.Generation);
        var fact = await probe.NextAsync(ct: ct);
        Assert.Equal(next.Generation, fact.Generation);
        Assert.Equal(TimerResolution.Recovered, fact.Resolution);
        await timer.InvokeDue(next.Generation);
        var sentinel = await timer.Schedule(600, "sentinel");
        time.Advance(TimeSpan.FromSeconds(600));
        await timer.InvokeDue(sentinel.Generation);
        Assert.Equal(sentinel.Generation, (await probe.NextAsync(ct: ct)).Generation);
        Assert.Equal(2, probe.Snapshot.Count);
    }

    [Fact]
    public async Task FailedElapsedWriteDoesNotPublishAndCanBeRetried()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new ManualClock();
        var faults = new StorageFaults();
        await using var host = await TimerTestSupport.StartAsync(ct, faults: faults, time: time);
        var timer = host.Brain.Get<ITimerTestDriver>("refuse-due");
        await using var probe = await host.ObserveAsync<TimerElapsed>(timer, ct);
        var scheduled = await timer.Schedule(600, "tea");
        time.Advance(TimeSpan.FromSeconds(600));
        faults.FailNextWrite(timer.GetGrainId());
        await Assert.ThrowsAsync<OrleansException>(() => timer.InvokeDue(scheduled.Generation));
        Assert.Equal(scheduled, await timer.Read());
        await timer.InvokeDue(scheduled.Generation);
        Assert.Equal(scheduled.Generation, (await probe.NextAsync(ct: ct)).Generation);
        Assert.Equal(TimerStatus.Elapsed, (await timer.Read()).Status);
        Assert.Single(probe.Snapshot);
    }

    [Fact]
    public async Task ReminderFailuresRespectStateCommitOrdering()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new ManualClock();
        var faults = new StorageFaults();
        var reminders = new ReminderFaults { FailRegister = true };
        await using var host = await TimerTestSupport.StartAsync(ct, faults: faults, time: time, reminders: reminders);
        var timer = host.Brain.Get<ITimerTestDriver>("refuse-reminder");
        await Assert.ThrowsAsync<IOException>(() => timer.Schedule(600, "tea"));
        Assert.Equal(TimerStatus.Unscheduled, (await timer.Read()).Status);
        faults.FailNextWrite(timer.GetGrainId());
        await Assert.ThrowsAsync<OrleansException>(() => timer.Schedule(600, "tea"));
        Assert.Equal(0, (await timer.Read()).Generation);
        var scheduled = await timer.Schedule(600, "tea");
        reminders.FailUnregister = true;
        await Assert.ThrowsAsync<IOException>(timer.Stop);
        Assert.Equal(TimerStatus.Cancelled, (await timer.Read()).Status);
        time.Advance(TimeSpan.FromSeconds(600));
        await timer.InvokeDue(scheduled.Generation);
        Assert.Equal(TimerStatus.Cancelled, (await timer.Read()).Status);
    }
}
