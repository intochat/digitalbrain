using DigitalBrain.Testing;

using DigitalBrain.Core.Tests.Support;

namespace DigitalBrain.Core.Tests.Physics;

public sealed class ScheduleTickTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    private static readonly TimeSpan TickPeriod = TimeSpan.FromMilliseconds(100);

    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain.AddModule<Pulse>().AddModule<PulseObserver>();

    [Fact(DisplayName = "A scheduled fact arrives as a heard entry after the period and the handler may Emit without a second bus")]
    public async Task ScheduledFactArrivesAsOrdinarySelfTurn()
    {
        var ct = Cancellation;
        var session = Brain.Session("pulse-tick");
        var pulseId = new NeuronId("pulse", "pulse-tick");
        var observerId = new NeuronId("pulseobserver", "pulse-tick");

        await session.EmitAsync(new StartPulse(TickPeriod), ct);

        var pulseAfterArm = await WaitForJournalAsync(
            pulseId,
            reading => reading.AllSaid<Schedule>().Count == 1,
            "a said Schedule arming the tick",
            ct);
        var scheduleSaid = pulseAfterArm.SaidSingle<Schedule>();
        Assert.Equal(TickPeriod, Assert.IsType<Schedule>(scheduleSaid.Body).Period);

        await Clock.AdvanceAsync(TickPeriod, ct);

        var pulseReading = await WaitForJournalAsync(
            pulseId,
            reading => reading.AllHeard<Tick>().Count >= 1 && reading.AllSaid<PulseBeat>().Count >= 1,
            "a heard Tick and a said PulseBeat",
            ct);

        var tickHeard = pulseReading.AllHeard<Tick>()[0];
        Assert.Equal(pulseId, tickHeard.Metadata.Source);
        Assert.Equal(new SynapseRef(pulseId, scheduleSaid.Position), tickHeard.Cause);

        var beatSaid = pulseReading.AllSaid<PulseBeat>()[0];
        Assert.Equal("declared", beatSaid.DeliveryTo(observerId).Via);
        Assert.Equal(new SynapseRef(pulseId, tickHeard.Position), beatSaid.Cause);

        var observerReading = await WaitForJournalAsync(
            observerId,
            reading => reading.AllHeard<PulseBeat>().Count >= 1,
            "a heard PulseBeat on the observer",
            ct);
        var beatHeard = observerReading.AllHeard<PulseBeat>()[0];
        Assert.Equal(pulseId, beatHeard.Metadata.Source);
        Assert.Equal(beatSaid.Position, beatHeard.Metadata.Sequence);
    }
}

public sealed class ScheduleFailedTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    private static readonly TimeSpan TickPeriod = TimeSpan.FromMilliseconds(50);

    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain.AddModule<FailingPulse>();

    [Fact(DisplayName = "After the schedule failure limit consecutive tick failures Core journals ScheduleFailed and removes the entry")]
    public async Task ConsecutiveTickFailuresJournalScheduleFailedAndUnschedule()
    {
        var ct = Cancellation;
        var session = Brain.Session("pulse-fail");
        var pulseId = new NeuronId("failingpulse", "pulse-fail");

        await session.EmitAsync(new StartPulse(TickPeriod), ct);
        _ = await WaitForJournalAsync(
            pulseId,
            reading => reading.AllSaid<Schedule>().Count == 1,
            "a said Schedule arming the failing tick",
            ct);

        var bound = TimeSpan.FromTicks(TickPeriod.Ticks * (DeliveryPolicy.ScheduleFailureLimit + 2));
        await Clock.AdvanceAsync(bound, ct);

        var failedReading = await WaitForJournalAsync(
            pulseId,
            reading => reading.AllSaid<ScheduleFailed>().Count == 1,
            "a said ScheduleFailed after the consecutive failure limit",
            ct);

        var failed = Assert.IsType<ScheduleFailed>(failedReading.SaidSingle<ScheduleFailed>().Body);
        Assert.Equal(NeuronId.KindOf(typeof(Tick)), failed.Fact);
        Assert.Equal(DeliveryPolicy.ScheduleFailureLimit, failed.ConsecutiveFailures);
        Assert.Contains("scheduled tick refused", failed.Reason, StringComparison.Ordinal);

        // Further wall time must not mint another terminal — the row is gone.
        await Clock.AdvanceAsync(TickPeriod * 3, ct);
        await Task.Delay(TickPeriod * 3, ct);

        var after = await ReadAsync(pulseId, ct);
        Assert.Single(after.AllSaid<ScheduleFailed>());
        Assert.Empty(after.AllSaid<PulseBeat>());
    }
}

public sealed class ScheduleUnscheduleTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    private static readonly TimeSpan TickPeriod = TimeSpan.FromMilliseconds(100);

    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain.AddModule<SteadyPulse>().AddModule<PulseObserver>();

    [Fact(DisplayName = "Unschedule removes the armed row so further clock advances deliver no more ticks")]
    public async Task UnscheduleStopsFurtherTicks()
    {
        var ct = Cancellation;
        var session = Brain.Session("pulse-stop");
        var pulseId = new NeuronId("steadypulse", "pulse-stop");

        await session.EmitAsync(new StartPulse(TickPeriod), ct);
        await Clock.AdvanceAsync(TickPeriod, ct);

        _ = await WaitForJournalAsync(
            pulseId,
            reading => reading.AllHeard<Tick>().Count >= 1,
            "the first heard Tick",
            ct);

        await session.EmitAsync(new StopPulse(), ct);
        // Count ticks only after Unschedule is durable — a tick can land between first
        // observation and stop settlement under parallel suite load.
        var stopped = await WaitForJournalAsync(
            pulseId,
            reading => reading.AllSaid<Unschedule>().Count == 1,
            "a said Unschedule",
            ct);
        var ticksAtStop = stopped.AllHeard<Tick>().Count;
        Assert.True(ticksAtStop >= 1);

        await Clock.AdvanceAsync(TickPeriod * 3, ct);
        await Task.Delay(TickPeriod * 3, ct);

        var afterStop = await ReadAsync(pulseId, ct);
        Assert.Equal(ticksAtStop, afterStop.AllHeard<Tick>().Count);
        Assert.Single(afterStop.AllSaid<Unschedule>());
    }
}
