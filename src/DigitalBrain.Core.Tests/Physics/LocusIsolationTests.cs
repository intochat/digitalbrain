using DigitalBrain.Testing;

using DigitalBrain.Core.Tests.Support;

namespace DigitalBrain.Core.Tests.Physics;

public sealed class LocusIsolationTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain.AddModule<Planner>().AddModule<Diary>();

    [Fact(DisplayName = "Parallel sessions at different context names never deliver declared fan-out into each other's journals")]
    public async Task ContextNamesIsolateDeclaredFanOut()
    {
        var ct = Cancellation;
        var sessionA = Brain.Session("a");
        var sessionB = Brain.Session("b");
        var plannerA = new NeuronId("planner", "a");
        var plannerB = new NeuronId("planner", "b");
        var diaryA = new NeuronId("diary", "a");
        var diaryB = new NeuronId("diary", "b");
        var dateA = new DateOnly(2026, 8, 1);
        var dateB = new DateOnly(2026, 8, 2);

        await sessionA.EmitAsync(new PlanDay(dateA), ct);

        var diaryAReading = await WaitForJournalAsync(
            diaryA,
            reading => reading.AllHeard<DayPlanned>().Count == 1,
            "diary@a heard DayPlanned from session a",
            ct);

        var plannedA = Assert.IsType<DayPlanned>(diaryAReading.HeardSingle<DayPlanned>().Body);
        Assert.Equal(dateA, plannedA.Date);

        var wrongDiaryAfterA = await ReadAsync(diaryB, ct);
        Assert.Empty(wrongDiaryAfterA.Journal);

        var wrongPlannerAfterA = await ReadAsync(plannerB, ct);
        Assert.Empty(wrongPlannerAfterA.Journal);

        var sessionAReading = await ReadAsync(sessionA.Id, ct);
        var planSaidA = sessionAReading.SaidSingle<PlanDay>();
        Assert.Equal("declared", planSaidA.DeliveryTo(plannerA).Via);
        Assert.Null(planSaidA.DeliveryToOrNull(plannerB));

        var plannerAReading = await ReadAsync(plannerA, ct);
        var dayPlannedSaidA = plannerAReading.SaidSingle<DayPlanned>();
        Assert.Equal("declared", dayPlannedSaidA.DeliveryTo(diaryA).Via);
        Assert.Null(dayPlannedSaidA.DeliveryToOrNull(diaryB));

        await sessionB.EmitAsync(new PlanDay(dateB), ct);

        var diaryBReading = await WaitForJournalAsync(
            diaryB,
            reading => reading.AllHeard<DayPlanned>().Count == 1,
            "diary@b heard DayPlanned from session b",
            ct);

        var plannedB = Assert.IsType<DayPlanned>(diaryBReading.HeardSingle<DayPlanned>().Body);
        Assert.Equal(dateB, plannedB.Date);

        diaryAReading = await ReadAsync(diaryA, ct);
        Assert.Single(diaryAReading.AllHeard<DayPlanned>());
        Assert.Equal(dateA, Assert.IsType<DayPlanned>(diaryAReading.HeardSingle<DayPlanned>().Body).Date);

        var plannerBReading = await ReadAsync(plannerB, ct);
        var dayPlannedSaidB = plannerBReading.SaidSingle<DayPlanned>();
        Assert.Equal("declared", dayPlannedSaidB.DeliveryTo(diaryB).Via);
        Assert.Null(dayPlannedSaidB.DeliveryToOrNull(diaryA));

        Assert.DoesNotContain(
            (await ReadAsync(plannerA, ct)).AllHeard<PlanDay>(),
            fact => fact.Metadata.Source == sessionB.Id);
        Assert.DoesNotContain(
            (await ReadAsync(diaryA, ct)).AllHeard<DayPlanned>(),
            fact => Assert.IsType<DayPlanned>(fact.Body).Date == dateB);
        Assert.DoesNotContain(
            (await ReadAsync(diaryB, ct)).AllHeard<DayPlanned>(),
            fact => Assert.IsType<DayPlanned>(fact.Body).Date == dateA);
    }
}
