using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class ReminderWakesDormantTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    // Absolute month-scale due: proves schedule table + reactivation re-arm, not a short live timer.
    private static readonly TimeSpan ThirtyDays = TimeSpan.FromDays(30);

    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<ContractReview>()
            .AddModule<ReminderSurfaceLedger>();

    [Fact(DisplayName = "Reminder wakes dormant neuron after 30d: schedule survives deactivation")]
    public async Task ScheduleSurvivesDeactivationAndDeliversDueTick()
    {
        var ct = Cancellation;
        var context = "acme-contract";
        var session = Brain.Session(context);
        var reviewId = new NeuronId("contractreview", context);
        var ledgerId = new NeuronId("remindersurfaceledger", context);
        var contractId = "acme-msa-2026";

        await session.EmitAsync(new ArmContractReview(contractId, ThirtyDays), ct);

        var armed = await WaitForJournalAsync(
            reviewId,
            reading => reading.AllSaid<Schedule>().Count == 1
                && reading.AllHeard<ArmContractReview>().Count == 1,
            "Schedule armed for ContractReviewDue",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var armSaid = sessionReading.SaidSingle<ArmContractReview>();
        Assert.Equal("declared", armSaid.DeliveryTo(reviewId).Via);

        var armHeard = armed.HeardSingle<ArmContractReview>();
        Assert.Equal(session.Id, armHeard.Metadata.Source);
        Assert.Equal(armSaid.Position, armHeard.Metadata.Sequence);

        var scheduleSaid = armed.SaidSingle<Schedule>();
        Assert.Equal(new SynapseRef(session.Id, armSaid.Position), scheduleSaid.Cause);
        var schedule = Assert.IsType<Schedule>(scheduleSaid.Body);
        Assert.Equal(ThirtyDays, schedule.Period);
        Assert.IsType<ContractReviewDue>(schedule.Fact);
        Assert.Equal(contractId, Assert.IsType<ContractReviewDue>(schedule.Fact).ContractId);

        // No tick while due is still a month away.
        Assert.Empty(armed.AllHeard<ContractReviewDue>());
        Assert.Empty(armed.AllSaid<ContractReminderSurfaced>());

        await DeactivateAsync([reviewId, session.Id], ct);

        // Controllable clock past NextDue; reactivation re-arms grain timer with DueTime.Zero.
        await Clock.AdvanceAsync(ThirtyDays, ct);

        var afterWake = await WaitForJournalAsync(
            reviewId,
            reading => reading.AllHeard<ContractReviewDue>().Count == 1
                && reading.AllSaid<ContractReminderSurfaced>().Count == 1
                && reading.AllSaid<Unschedule>().Count == 1,
            "ContractReviewDue heard and reminder surfaced after dormant wake",
            ct);

        var dueHeard = afterWake.HeardSingle<ContractReviewDue>();
        Assert.Equal(reviewId, dueHeard.Metadata.Source);
        Assert.Equal(new SynapseRef(reviewId, scheduleSaid.Position), dueHeard.Cause);
        Assert.Equal(contractId, Assert.IsType<ContractReviewDue>(dueHeard.Body).ContractId);

        var surfacedSaid = afterWake.SaidSingle<ContractReminderSurfaced>();
        Assert.Equal(new SynapseRef(reviewId, dueHeard.Position), surfacedSaid.Cause);
        Assert.Equal("declared", surfacedSaid.DeliveryTo(ledgerId).Via);
        Assert.Equal(contractId, Assert.IsType<ContractReminderSurfaced>(surfacedSaid.Body).ContractId);

        var ledgerReading = await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<ContractReminderSurfaced>().Count == 1,
            "ledger heard ContractReminderSurfaced",
            ct);
        var surfacedHeard = ledgerReading.HeardSingle<ContractReminderSurfaced>();
        Assert.Equal(reviewId, surfacedHeard.Metadata.Source);
        Assert.Equal(surfacedSaid.Position, surfacedHeard.Metadata.Sequence);

        // One-shot: Unschedule after surface; further advances must not re-fire.
        await Clock.AdvanceAsync(ThirtyDays, ct);
        await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
        var afterIdle = await ReadAsync(reviewId, ct);
        Assert.Single(afterIdle.AllHeard<ContractReviewDue>());
        Assert.Single(afterIdle.AllSaid<ContractReminderSurfaced>());
        Assert.Single(afterIdle.AllSaid<Unschedule>());
    }
}
