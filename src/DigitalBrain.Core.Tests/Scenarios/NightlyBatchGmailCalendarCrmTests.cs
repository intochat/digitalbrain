using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class NightlyBatchGmailCalendarCrmTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    private static readonly TimeSpan DueIn = TimeSpan.FromHours(2);

    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<NightlyReconcile>()
            .AddModule<NightlyGmailSource>()
            .AddModule<NightlyCalendarSource>()
            .AddModule<NightlyCrmSource>()
            .AddModule<NightlyPackLedger>();

    [Fact(DisplayName =
        "Nightly batch: Schedule NightlyReconcileDue → Gmail+Calendar+CRM section asks join → NightlyMorningPackReady")]
    public async Task ScheduledDueFansOutSectionsIntoMorningPack()
    {
        var ct = Cancellation;
        var context = "nightly-2026-08-05";
        var session = Brain.Session(context);
        var reconcileId = new NeuronId("nightlyreconcile", context);
        var ledgerId = new NeuronId("nightlypackledger", context);
        var dayKey = "2026-08-05";

        await session.EmitAsync(new ArmNightlyReconcile(dayKey, DueIn), ct);

        var armed = await WaitForJournalAsync(
            reconcileId,
            reading => reading.AllSaid<Schedule>().Count == 1
                && reading.AllHeard<ArmNightlyReconcile>().Count == 1,
            "nightly schedule armed",
            ct);

        var scheduleSaid = armed.SaidSingle<Schedule>();
        Assert.IsType<NightlyReconcileDue>(Assert.IsType<Schedule>(scheduleSaid.Body).Fact);

        // Match S46: deactivate so the next Read re-arms the schedule timer from journaled NextDue.
        await DeactivateAsync([reconcileId, session.Id], ct);
        await Clock.AdvanceAsync(DueIn, ct);

        var afterPack = await WaitForJournalAsync(
            reconcileId,
            reading => reading.AllHeard<NightlyReconcileDue>().Count == 1
                && reading.AllHeard<NightlyGmailSection>().Count == 1
                && reading.AllHeard<NightlyCalendarSection>().Count == 1
                && reading.AllHeard<NightlyCrmSection>().Count == 1
                && reading.AllSaid<NightlyMorningPackReady>().Count == 1,
            "due tick → three sections → pack ready",
            ct);

        var dueHeard = afterPack.HeardSingle<NightlyReconcileDue>();
        Assert.Equal(reconcileId, dueHeard.Metadata.Source);
        Assert.Equal(new SynapseRef(reconcileId, scheduleSaid.Position), dueHeard.Cause);

        Assert.Equal("ask", afterPack.SaidSingle<NightlyGmailSectionAsked>()
            .DeliveryTo(new NeuronId("nightlygmailsource", context)).Via);
        Assert.Equal("ask", afterPack.SaidSingle<NightlyCalendarSectionAsked>()
            .DeliveryTo(new NeuronId("nightlycalendarsource", context)).Via);
        Assert.Equal("ask", afterPack.SaidSingle<NightlyCrmSectionAsked>()
            .DeliveryTo(new NeuronId("nightlycrmsource", context)).Via);

        var packSaid = afterPack.SaidSingle<NightlyMorningPackReady>();
        Assert.Equal("declared", packSaid.DeliveryTo(ledgerId).Via);
        var pack = Assert.IsType<NightlyMorningPackReady>(packSaid.Body);
        Assert.Equal(dayKey, pack.DayKey);
        Assert.Equal("3 unanswered VIP threads", pack.Gmail);
        Assert.Equal("2 gaps before 10:00", pack.Calendar);
        Assert.Equal("1 opp missing next step", pack.Crm);

        var ledgerReading = await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<NightlyMorningPackReady>().Count == 1,
            "ledger heard morning pack",
            ct);
        Assert.Equal(reconcileId, ledgerReading.HeardSingle<NightlyMorningPackReady>().Metadata.Source);
        Assert.Equal(packSaid.Position, ledgerReading.HeardSingle<NightlyMorningPackReady>().Metadata.Sequence);
    }
}
