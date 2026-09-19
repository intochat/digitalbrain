using DigitalBrain.Time.Timers.Signals;
using Orleans;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TimerFacts
{
    [Fact]
    public async Task OneShotReleasesItsRegistrationAndStopIsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var control = new ControlledTimers();
        await using var host = await TimerTestSupport.StartAsync(ct, timers: control);
        var timer = host.Brain.Get<ITimer>("once");
        await using var ticks = await host.ObserveAsync<TimerTick>(timer, ct);
        await timer.Start(TimeSpan.FromDays(1));
        var registration = control.For(timer.GetGrainId());
        Assert.Equal(TimeSpan.FromDays(1), registration.Options.DueTime);
        Assert.Equal(Timeout.InfiniteTimeSpan, registration.Options.Period);
        Assert.True(registration.Options.KeepAlive);
        Assert.False(registration.Options.Interleave);
        Assert.Empty(ticks.Snapshot);
        await registration.FireAsync(ct);
        Assert.Equal("once", (await ticks.NextAsync(ct: ct)).TimerId);
        Assert.True(registration.IsDisposed);
        await timer.Stop();
        await timer.Stop();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => registration.FireAsync(ct));
    }

    [Fact]
    public async Task ReplacementDisposesPreviousScheduleAndPeriodicTicksContinueUntilStopped()
    {
        var ct = TestContext.Current.CancellationToken;
        var control = new ControlledTimers();
        await using var host = await TimerTestSupport.StartAsync(ct, timers: control);
        var timer = host.Brain.Get<ITimer>("replace");
        await using var ticks = await host.ObserveAsync<TimerTick>(timer, ct);
        await timer.Start(TimeSpan.FromDays(1));
        var previous = control.For(timer.GetGrainId());
        await timer.Start(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
        var current = control.For(timer.GetGrainId());
        Assert.True(previous.IsDisposed);
        Assert.Equal(TimeSpan.FromSeconds(3), current.Options.Period);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => previous.FireAsync(ct));
        await current.FireAsync(ct);
        await ticks.NextAsync(ct: ct);
        await current.FireAsync(ct);
        await ticks.NextAsync(ct: ct);
        Assert.False(current.IsDisposed);
        Assert.Equal(2, ticks.Snapshot.Count);
        await timer.Stop();
        Assert.True(current.IsDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => current.FireAsync(ct));
    }

    [Fact]
    public async Task InvalidReplacementPreservesTheWorkingTimer()
    {
        var ct = TestContext.Current.CancellationToken;
        var control = new ControlledTimers();
        await using var host = await TimerTestSupport.StartAsync(ct, timers: control);
        var timer = host.Brain.Get<ITimer>("invalid");
        await using var ticks = await host.ObserveAsync<TimerTick>(timer, ct);
        await timer.Start(TimeSpan.Zero);
        var original = control.For(timer.GetGrainId());
        foreach (var due in new[] { TimeSpan.FromTicks(-1), Timeout.InfiniteTimeSpan, TimeSpan.MaxValue })
        { await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => timer.Start(due)); }
        foreach (var period in new[] { TimeSpan.Zero, TimeSpan.FromTicks(-1), TimeSpan.MaxValue })
        { await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => timer.Start(TimeSpan.Zero, period)); }
        Assert.Same(original, control.For(timer.GetGrainId()));
        await original.FireAsync(ct);
        Assert.Equal("invalid", (await ticks.NextAsync(ct: ct)).TimerId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeactivationLosesTheScheduleAndRequiresAnotherStart(bool restartSilo)
    {
        var ct = TestContext.Current.CancellationToken;
        var control = new ControlledTimers();
        await using var host = await TimerTestSupport.StartAsync(ct, timers: control);
        var timer = host.Brain.Get<ITimer>("deactivate");
        await timer.Start(TimeSpan.FromDays(1));
        var original = control.For(timer.GetGrainId());
        if (restartSilo) { await host.RestartSiloAsync(ct); }
        else { await host.DeactivateAsync(timer, ct); }
        Assert.True(original.IsDisposed);
        // Stop activates a fresh grain but must not recreate the discarded timer.
        await timer.Stop();
        Assert.Same(original, control.For(timer.GetGrainId()));
        await using var ticks = await host.ObserveAsync<TimerTick>(timer, ct);
        await timer.Start(TimeSpan.Zero);
        var current = control.For(timer.GetGrainId());
        Assert.NotSame(original, current);
        await current.FireAsync(ct);
        Assert.Equal("deactivate", (await ticks.NextAsync(ct: ct)).TimerId);
    }

    [Fact]
    public async Task TicksWithoutASubscriberAreNotReplayed()
    {
        var ct = TestContext.Current.CancellationToken;
        var control = new ControlledTimers();
        await using var host = await TimerTestSupport.StartAsync(ct, timers: control);
        var timer = host.Brain.Get<ITimer>("live-only");
        await timer.Start(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var registration = control.For(timer.GetGrainId());
        await registration.FireAsync(ct);
        var subscribedAt = control.UtcNow += TimeSpan.FromSeconds(1);
        await using var ticks = await host.ObserveAsync<TimerTick>(timer, ct);
        await registration.FireAsync(ct);
        Assert.True((await ticks.NextAsync(ct: ct)).ObservedAt >= subscribedAt);
        await timer.Stop();
        Assert.Single(ticks.Snapshot);
    }

    [Fact]
    public async Task ObsoleteCallbacksAfterReplacementAndStopCannotPublish()
    {
        var ct = TestContext.Current.CancellationToken;
        var control = new ControlledTimers();
        await using var host = await TimerTestSupport.StartAsync(ct, timers: control);
        var timer = host.Brain.Get<ITimer>("obsolete");
        await using var ticks = await host.ObserveAsync<TimerTick>(timer, ct);
        await timer.Start(TimeSpan.FromDays(1));
        var old = control.For(timer.GetGrainId());
        await timer.Start(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var replacement = control.For(timer.GetGrainId());
        await old.DeliverObsoleteAsync(ct);
        await timer.Stop();
        await replacement.DeliverObsoleteAsync(ct);
        var expected = control.UtcNow += TimeSpan.FromSeconds(1);
        await timer.Start(TimeSpan.Zero);
        await control.For(timer.GetGrainId()).FireAsync(ct);
        Assert.Equal(expected, (await ticks.NextAsync(ct: ct)).ObservedAt);
        Assert.Single(ticks.Snapshot);
    }

    [Fact]
    public async Task LongDueTimePreventsIdleCollectionAndStopOrCompletionReleasesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var control = new ControlledTimers();
        await using var host = await TimerTestSupport.StartAsync(ct, timers: control, fastCollection: true);
        var timer = host.Brain.Get<ITimer>("long-delay");
        await timer.Start(TimeSpan.FromDays(1));
        var active = control.For(timer.GetGrainId());
        var sentinel = host.Brain.Get<ITimer>("idle-sentinel");
        await sentinel.Start(TimeSpan.Zero);
        var completed = control.For(sentinel.GetGrainId());
        await completed.FireAsync(ct);
        await completed.Context.Deactivated.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.False(active.Context.Deactivated.IsCompleted);
        await timer.Stop();
        await active.Context.Deactivated.WaitAsync(TimeSpan.FromSeconds(5), ct);
    }

    [Fact]
    public async Task SupportedDurationBoundariesCanBeRegistered()
    {
        var ct = TestContext.Current.CancellationToken;
        var control = new ControlledTimers();
        await using var host = await TimerTestSupport.StartAsync(ct, timers: control);
        var timer = host.Brain.Get<ITimer>("bounds");
        var maximum = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
        foreach (var duration in new[] { TimeSpan.FromTicks(1), maximum })
        {
            await timer.Start(duration, duration);
            var registration = control.For(timer.GetGrainId());
            Assert.Equal(duration, registration.Options.DueTime);
            Assert.Equal(duration, registration.Options.Period);
        }
        await timer.Stop();
    }

    [Fact]
    public async Task RealPeriodicTimerCanBeStopped()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TimerTestSupport.StartAsync(ct);
        var timer = host.Brain.Get<ITimer>("periodic");
        await using var ticks = await host.ObserveAsync<TimerTick>(timer, ct);
        await timer.Start(TimeSpan.Zero, TimeSpan.FromMilliseconds(20));
        await ticks.NextAsync(ct: ct);
        await ticks.NextAsync(ct: ct);
        await timer.Stop();
    }
}
