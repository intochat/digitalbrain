using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests;

public sealed class PlannerDiaryTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain.AddModule<Planner>().AddModule<Diary>();

    [Fact(DisplayName = "A planned day reaches the bodiless diary by declaration alone, with the planner as source")]
    public async Task DiaryHearsByDeclaration()
    {
        var ct = Cancellation;
        var date = new DateOnly(2026, 8, 7);
        var session = Brain.Session("day-7");
        var plannerId = new NeuronId("planner", "day-7");
        var diaryId = new NeuronId("diary", "day-7");

        await session.EmitAsync(new PlanDay(date), ct);

        var planned = await WaitForAsync<DayPlanned>(diaryId, ct);
        Assert.Equal(date, planned.Date);
        string[] expectedTasks = ["write core", "walk"];
        Assert.Equal(expectedTasks, planned.Tasks);

        var sessionReading = await ReadAsync(session.Id, ct);
        var utterance = sessionReading.SaidSingle<PlanDay>();
        Assert.Equal("declared", utterance.DeliveryTo(plannerId).Via);
        Assert.Null(utterance.Cause);

        var plannerReading = await ReadAsync(plannerId, ct);
        var planHeard = plannerReading.HeardSingle<PlanDay>();
        Assert.Equal(session.Id, planHeard.Metadata.Source);
        Assert.Equal(utterance.Position, planHeard.Metadata.Sequence);

        var plannedSaid = plannerReading.SaidSingle<DayPlanned>();
        Assert.Equal("declared", plannedSaid.DeliveryTo(diaryId).Via);
        Assert.Equal(new SynapseRef(session.Id, utterance.Position), plannedSaid.Cause);

        var diaryReading = await ReadAsync(diaryId, ct);
        var reception = diaryReading.HeardSingle<DayPlanned>();
        Assert.Equal(plannerId, reception.Metadata.Source);
        Assert.Equal(plannedSaid.Position, reception.Metadata.Sequence);
    }
}
