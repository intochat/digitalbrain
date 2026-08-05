using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class MidstreamCorrectCancelReplanTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<CancelableResearchRunner>()
            .AddModule<CancelReplanUiProjector>();

    [Fact(DisplayName = "Midstream cancel: old-generation Progress freezes; ReplanStarted new generation")]
    public async Task CancelFreezesOldGenerationAndStartsReplan()
    {
        var ct = Cancellation;
        var context = "research-12";
        var session = Brain.Session(context);
        var runnerId = new NeuronId("cancelableresearchrunner", context);
        var uiId = new NeuronId("cancelreplanuiprojector", context);
        var goal = "draft three emails about the renewal";

        await session.EmitAsync(new CancelableResearchAsked(goal), ct);

        var runnerLive = await WaitForJournalAsync(
            runnerId,
            reading => reading.AllSaid<CancelableResearchStarted>().Count == 1
                && reading.AllSaid<CancelableResearchProgress>().Count >= 1
                && reading.AllSaid<Schedule>().Count == 1,
            "ResearchStarted, at least one Progress, and Schedule armed",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var askedSaid = sessionReading.SaidSingle<CancelableResearchAsked>();
        Assert.Equal("declared", askedSaid.DeliveryTo(runnerId).Via);

        var askedHeard = runnerLive.HeardSingle<CancelableResearchAsked>();
        Assert.Equal(session.Id, askedHeard.Metadata.Source);
        Assert.Equal(askedSaid.Position, askedHeard.Metadata.Sequence);

        var startedSaid = runnerLive.SaidSingle<CancelableResearchStarted>();
        Assert.Equal(new SynapseRef(session.Id, askedSaid.Position), startedSaid.Cause);
        Assert.Equal("declared", startedSaid.DeliveryTo(uiId).Via);
        var started = Assert.IsType<CancelableResearchStarted>(startedSaid.Body);
        Assert.Equal(1, started.Generation);
        Assert.Equal(goal, started.Goal);

        var progressBeforeCancel = runnerLive.AllSaid<CancelableResearchProgress>()
            .Where(said => Assert.IsType<CancelableResearchProgress>(said.Body).Generation == 1)
            .ToArray();
        Assert.NotEmpty(progressBeforeCancel);
        Assert.All(progressBeforeCancel, said =>
        {
            Assert.Equal(1, Assert.IsType<CancelableResearchProgress>(said.Body).Generation);
            Assert.Equal("declared", said.DeliveryTo(uiId).Via);
        });

        await session.EmitAsync(new CancelableUserCancel("only draft Priya, shorter"), ct);

        var runnerCancelled = await WaitForJournalAsync(
            runnerId,
            reading => reading.AllSaid<CancelableResearchCancelled>().Count == 1
                && reading.AllSaid<CancelableReplanStarted>().Count == 1,
            "ResearchCancelled and ReplanStarted",
            ct);

        var sessionAfter = await ReadAsync(session.Id, ct);
        var cancelSaid = sessionAfter.SaidSingle<CancelableUserCancel>();
        Assert.Equal("declared", cancelSaid.DeliveryTo(runnerId).Via);

        var cancelHeard = runnerCancelled.HeardSingle<CancelableUserCancel>();
        Assert.Equal(session.Id, cancelHeard.Metadata.Source);
        Assert.Equal(cancelSaid.Position, cancelHeard.Metadata.Sequence);

        var cancelledSaid = runnerCancelled.SaidSingle<CancelableResearchCancelled>();
        Assert.Equal(new SynapseRef(session.Id, cancelSaid.Position), cancelledSaid.Cause);
        Assert.Equal("declared", cancelledSaid.DeliveryTo(uiId).Via);
        var cancelled = Assert.IsType<CancelableResearchCancelled>(cancelledSaid.Body);
        Assert.Equal(1, cancelled.Generation);
        Assert.Equal("only draft Priya, shorter", cancelled.Reason);

        var replanSaid = runnerCancelled.SaidSingle<CancelableReplanStarted>();
        Assert.Equal(new SynapseRef(session.Id, cancelSaid.Position), replanSaid.Cause);
        Assert.Equal("declared", replanSaid.DeliveryTo(uiId).Via);
        var replan = Assert.IsType<CancelableReplanStarted>(replanSaid.Body);
        Assert.Equal(2, replan.Generation);
        Assert.Equal(goal, replan.Goal);

        var gen1AtCancel = runnerCancelled.AllSaid<CancelableResearchProgress>()
            .Count(said => Assert.IsType<CancelableResearchProgress>(said.Body).Generation == 1);

        // More wall time + controllable advance so the scheduled pulse keeps firing.
        await Clock.AdvanceAsync(CancelableResearchRunner.PulsePeriod * 4, ct);
        await Task.Delay(CancelableResearchRunner.PulsePeriod * 4, ct);

        var runnerAfter = await WaitForJournalAsync(
            runnerId,
            reading => reading.AllSaid<CancelableResearchProgress>()
                .Any(said => Assert.IsType<CancelableResearchProgress>(said.Body).Generation == 2),
            "at least one ResearchProgress for generation 2 after replan",
            ct);

        var gen1After = runnerAfter.AllSaid<CancelableResearchProgress>()
            .Count(said => Assert.IsType<CancelableResearchProgress>(said.Body).Generation == 1);
        Assert.Equal(gen1AtCancel, gen1After);

        var gen2Progress = runnerAfter.AllSaid<CancelableResearchProgress>()
            .Where(said => Assert.IsType<CancelableResearchProgress>(said.Body).Generation == 2)
            .ToArray();
        Assert.NotEmpty(gen2Progress);
        Assert.All(gen2Progress, said =>
        {
            Assert.True(said.Position > cancelledSaid.Position);
            Assert.Equal("declared", said.DeliveryTo(uiId).Via);
        });

        var uiReading = await WaitForJournalAsync(
            uiId,
            reading => reading.AllHeard<CancelableResearchCancelled>().Count == 1
                && reading.AllHeard<CancelableReplanStarted>().Count == 1
                && reading.AllHeard<CancelableResearchProgress>()
                    .Any(heard => Assert.IsType<CancelableResearchProgress>(heard.Body).Generation == 2),
            "UI heard cancel, replan, and gen-2 progress",
            ct);

        Assert.Equal(runnerId, uiReading.HeardSingle<CancelableResearchCancelled>().Metadata.Source);
        Assert.Equal(cancelledSaid.Position, uiReading.HeardSingle<CancelableResearchCancelled>().Metadata.Sequence);
        Assert.Equal(runnerId, uiReading.HeardSingle<CancelableReplanStarted>().Metadata.Source);
        Assert.Equal(replanSaid.Position, uiReading.HeardSingle<CancelableReplanStarted>().Metadata.Sequence);
    }
}
