using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Messaging;
using DigitalBrain.Testing;
using Xunit;
using TimerModule = DigitalBrain.Time;

namespace DigitalBrain.Simulation.Tests;

[Collection(SimulationCollection.Name)]
public sealed class TimerReminderTests(SimulationFixture fixture)
{
    [Fact]
    public async Task SingleTimerElapsesThroughAnOrleansReminder()
    {
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("timer-owner"));
        var timerName = fixture.Sim.UniqueId("timer");
        var timer = brain.Get<TimerModule.ITimer>(timerName);
        var cancellationToken = TestContext.Current.CancellationToken;

        var armed = await timer.FireAsync(
            new TimerModule.StartTimer(CommandId.New(), DurationSeconds: 1, Note: "reminder check"),
            cancellationToken);

        var elapsed = await JournalWait.ForAsync(
            brain,
            timer.Id,
            JournalKind.Outgoing,
            delivery => delivery.Synapse is TimerModule.TimerElapsed timerElapsed
                && timerElapsed.Generation == armed.Generation,
            timeout: TimeSpan.FromSeconds(20));

        var timerElapsed = Assert.IsType<TimerModule.TimerElapsed>(elapsed.Synapse);
        Assert.Equal(TimerModule.TimerResolution.OnTime, timerElapsed.Resolution);
        Assert.Equal(TimerModule.TimerStatus.Elapsed, (await brain.GetGrainProxy<TimerModule.ITimer>(timerName).Read()).Status);
    }
}
