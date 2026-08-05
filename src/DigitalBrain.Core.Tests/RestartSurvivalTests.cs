using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests;

public sealed class RestartSurvivalTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain.AddModule<Planner>().AddModule<Diary>();

    [Fact(DisplayName = "Journals and watermarks survive deactivation and the brain keeps planning")]
    public async Task SurvivesDeactivation()
    {
        var ct = Cancellation;
        var session = Brain.Session("restart");
        var plannerId = new NeuronId("planner", "restart");
        var diaryId = new NeuronId("diary", "restart");

        await session.EmitAsync(new PlanDay(new DateOnly(2026, 8, 1)), ct);
        _ = await WaitForAsync<DayPlanned>(diaryId, ct);

        var plannerBefore = await ReadAsync(plannerId, ct);
        var diaryBefore = await ReadAsync(diaryId, ct);

        await DeactivateAsync([plannerId, diaryId, session.Id], ct);

        AssertSameJournal(plannerBefore, await ReadAsync(plannerId, ct));
        AssertSameJournal(diaryBefore, await ReadAsync(diaryId, ct));

        await session.EmitAsync(new PlanDay(new DateOnly(2026, 8, 2)), ct);

        var diaryAfter = await WaitForJournalAsync(
            diaryId,
            reading => reading.AllHeard<DayPlanned>().Count == 2,
            "a second heard DayPlanned",
            ct);
        var receptions = diaryAfter.AllHeard<DayPlanned>();
        DateOnly[] expectedDates = [new(2026, 8, 1), new(2026, 8, 2)];
        Assert.Equal(expectedDates, receptions.Select(reception => Assert.IsType<DayPlanned>(reception.Body).Date));
        Assert.Distinct(receptions.Select(reception => reception.Metadata.Sequence));
    }

    [Fact(DisplayName = "A sender crash around delivery forces redelivery and the watermark swallows the duplicate")]
    public async Task RedeliveredEmissionDoesNotDuplicate()
    {
        var ct = Cancellation;
        var session = Brain.Session("redelivery");
        var plannerId = new NeuronId("planner", "redelivery");
        var diaryId = new NeuronId("diary", "redelivery");

        await session.EmitAsync(new PlanDay(new DateOnly(2026, 8, 1)), ct);
        _ = await WaitForJournalAsync(
            diaryId, reading => reading.AllHeard<DayPlanned>().Count == 1, "the first heard DayPlanned", ct);

        // The next planner commit is allowed (its turn for day 2); the one after faults —
        // either the day-2 turn re-runs on a fresh activation or the settled delivery is
        // re-attempted from the journal. Both paths must converge on exactly one reception.
        var fault = FailNextJournalCommit(plannerId, allowCommitsBeforeFault: 1);

        await session.EmitAsync(new PlanDay(new DateOnly(2026, 8, 2)), ct);
        await fault.Consumed.WaitAsync(TimeSpan.FromSeconds(30), ct);

        _ = await WaitForJournalAsync(
            diaryId, reading => reading.AllHeard<DayPlanned>().Count == 2, "the second heard DayPlanned", ct);

        // A third day both reactivates the poisoned planner and, by per-receiver FIFO,
        // proves every earlier emission settled before it — redelivered, never duplicated.
        await session.EmitAsync(new PlanDay(new DateOnly(2026, 8, 3)), ct);

        var diaryReading = await WaitForJournalAsync(
            diaryId, reading => reading.AllHeard<DayPlanned>().Count == 3, "the third heard DayPlanned", ct);
        var receptions = diaryReading.AllHeard<DayPlanned>();
        DateOnly[] expectedDates = [new(2026, 8, 1), new(2026, 8, 2), new(2026, 8, 3)];
        Assert.Equal(expectedDates, receptions.Select(reception => Assert.IsType<DayPlanned>(reception.Body).Date));
        Assert.Distinct(receptions.Select(reception => reception.Metadata.Sequence));

        var plannerReading = await ReadAsync(plannerId, ct);
        Assert.Empty(plannerReading.AllSaid<DeliveryFailed>());
        Assert.Equal(3, plannerReading.AllSaid<DayPlanned>().Count);
    }

    private static void AssertSameJournal(NeuronReading before, NeuronReading after)
    {
        Assert.Equal(before.Journal.Count, after.Journal.Count);
        foreach (var (expected, survived) in before.Journal.Zip(after.Journal))
        {
            Assert.Equal(expected.Position, survived.Position);
            Assert.Equal(expected.Entry, survived.Entry);
            Assert.Equal(expected.Kind, survived.Kind);
            Assert.Equal(expected.Metadata, survived.Metadata);
        }
    }
}
