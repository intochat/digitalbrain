using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Testing;
using Xunit;
using TimerModule = DigitalBrain.Time;

namespace DigitalBrain.Simulation.Tests;

[Collection(SimulationCollection.Name)]
public sealed class TimerReminderTests(SimulationFixture fixture)
{
    [Fact]
    public async Task StartTimer_RecordsTimerScheduledExactlyOnce()
    {
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("timer-journal-owner"));
        var timer = brain.Get<TimerModule.ITimer>(fixture.Sim.UniqueId("timer-journal"));
        var commandId = CommandId.New();
        var cancellationToken = TestContext.Current.CancellationToken;

        await timer.SendAsync(
            new TimerModule.StartTimer(commandId, DurationSeconds: 30, Note: "single scheduled fact"),
            cancellationToken);

        var outgoing = await timer.ReadJournalAsync(
            JournalKind.Outgoing,
            cancellationToken: cancellationToken);

        Assert.Single(outgoing.Delta, delivery =>
            delivery.Signal is TimerModule.TimerScheduled scheduled
            && scheduled.CommandId == commandId);
    }

    [Fact]
    public async Task SingleTimerElapsesThroughAnOrleansReminder()
    {
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("timer-owner"));
        var timerName = fixture.Sim.UniqueId("timer");
        var timer = brain.Get<TimerModule.ITimer>(timerName);
        var cancellationToken = TestContext.Current.CancellationToken;

        var armed = await timer.RequestAsync(
            new TimerModule.StartTimer(CommandId.New(), DurationSeconds: 1, Note: "reminder check"),
            cancellationToken);

        var elapsed = await JournalWait.ForAsync(
            timer,
            JournalKind.Outgoing,
            delivery => delivery.Signal is TimerModule.TimerElapsed timerElapsed
                && timerElapsed.Generation == armed.Generation,
            timeout: TimeSpan.FromSeconds(20),
            cancellationToken: cancellationToken);

        var timerElapsed = Assert.IsType<TimerModule.TimerElapsed>(elapsed.Signal);
        Assert.Equal(TimerModule.TimerResolution.OnTime, timerElapsed.Resolution);
        Assert.Equal(TimerModule.TimerStatus.Elapsed, (await brain.GetGrainProxy<TimerModule.ITimer>(timerName).Read()).Status);
    }
}
