using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class LongRunningResearchProgressiveUiTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<ResearchJobOrchestrator>()
            .AddModule<CrmPipeline>()
            .AddModule<TranscriptSearch>()
            .AddModule<ResearchJobUiProjector>();

    [Fact(DisplayName =
        "Long-running research progressive UI: ResearchJobStarted → ≥2 ResearchJobProgress → ResearchJobCompleted; UI projector hears Progress in order")]
    public async Task ResearchJournalsOrderedProgressAndUiHearsThem()
    {
        var ct = Cancellation;
        var context = "research-q1";
        var session = Brain.Session(context);
        var orchestratorId = new NeuronId("researchjoborchestrator", context);
        var crmId = new NeuronId("crmpipeline", context);
        var transcriptId = new NeuronId("transcriptsearch", context);
        var uiId = new NeuronId("researchjobuiprojector", context);
        var jobId = "job-8841";
        var query = "pipeline vs closed-won last four quarters";

        await session.EmitAsync(new ResearchJobRequested(jobId, query), ct);

        var orchestratorReading = await WaitForJournalAsync(
            orchestratorId,
            reading => reading.AllSaid<ResearchJobStarted>().Count == 1
                && reading.AllSaid<ResearchJobProgress>().Count >= 2
                && reading.AllSaid<ResearchJobCompleted>().Count == 1,
            "ResearchJobStarted, ≥2 ResearchJobProgress, ResearchJobCompleted",
            ct);

        var uiReading = await WaitForJournalAsync(
            uiId,
            reading => reading.AllHeard<ResearchJobStarted>().Count == 1
                && reading.AllHeard<ResearchJobProgress>().Count >= 2
                && reading.AllHeard<ResearchJobCompleted>().Count == 1,
            "UI projector heard Started, ≥2 Progress, Completed",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var requestedSaid = sessionReading.SaidSingle<ResearchJobRequested>();
        Assert.Equal("declared", requestedSaid.DeliveryTo(orchestratorId).Via);

        var requestedHeard = orchestratorReading.HeardSingle<ResearchJobRequested>();
        Assert.Equal(session.Id, requestedHeard.Metadata.Source);
        Assert.Equal(requestedSaid.Position, requestedHeard.Metadata.Sequence);

        var startedSaid = orchestratorReading.SaidSingle<ResearchJobStarted>();
        Assert.Equal(new SynapseRef(session.Id, requestedSaid.Position), startedSaid.Cause);
        Assert.Equal("declared", startedSaid.DeliveryTo(uiId).Via);
        Assert.Equal(jobId, Assert.IsType<ResearchJobStarted>(startedSaid.Body).JobId);
        Assert.Equal(query, Assert.IsType<ResearchJobStarted>(startedSaid.Body).Query);

        var pipelineAsk = orchestratorReading.SaidSingle<PipelineSliceAsked>();
        Assert.Equal("ask", pipelineAsk.DeliveryTo(crmId).Via);
        Assert.Equal(new SynapseRef(session.Id, requestedSaid.Position), pipelineAsk.Cause);

        var crmReading = await ReadAsync(crmId, ct);
        var pipelineAnswered = crmReading.SaidSingle<PipelineSliceAnswered>();
        Assert.Equal(new SynapseRef(orchestratorId, pipelineAsk.Position), pipelineAnswered.Answers);

        var progress = orchestratorReading.AllSaid<ResearchJobProgress>()
            .OrderBy(said => said.Position)
            .ToArray();
        Assert.True(progress.Length >= 2, $"expected ≥2 ResearchJobProgress, got {progress.Length}");
        Assert.True(progress[0].Position < progress[1].Position);

        var firstProgress = Assert.IsType<ResearchJobProgress>(progress[0].Body);
        var secondProgress = Assert.IsType<ResearchJobProgress>(progress[1].Body);
        Assert.Equal(jobId, firstProgress.JobId);
        Assert.Equal("pipeline", firstProgress.Stage);
        Assert.Equal(50, firstProgress.Percent);
        Assert.Equal(jobId, secondProgress.JobId);
        Assert.Equal("mentions", secondProgress.Stage);
        Assert.Equal(100, secondProgress.Percent);
        Assert.Equal("declared", progress[0].DeliveryTo(uiId).Via);
        Assert.Equal("declared", progress[1].DeliveryTo(uiId).Via);

        var mentionsAsk = orchestratorReading.SaidSingle<CallMentionsAsked>();
        Assert.Equal("ask", mentionsAsk.DeliveryTo(transcriptId).Via);
        Assert.Equal(new SynapseRef(crmId, pipelineAnswered.Position), mentionsAsk.Cause);

        var transcriptReading = await ReadAsync(transcriptId, ct);
        var mentionsAnswered = transcriptReading.SaidSingle<CallMentionsAnswered>();
        Assert.Equal(new SynapseRef(orchestratorId, mentionsAsk.Position), mentionsAnswered.Answers);

        var completedSaid = orchestratorReading.SaidSingle<ResearchJobCompleted>();
        Assert.Equal(new SynapseRef(transcriptId, mentionsAnswered.Position), completedSaid.Cause);
        Assert.Equal("declared", completedSaid.DeliveryTo(uiId).Via);
        var completed = Assert.IsType<ResearchJobCompleted>(completedSaid.Body);
        Assert.Equal(jobId, completed.JobId);
        Assert.Contains("pipeline:", completed.Summary, StringComparison.Ordinal);
        Assert.Contains("mentions:", completed.Summary, StringComparison.Ordinal);

        var uiProgress = uiReading.AllHeard<ResearchJobProgress>()
            .OrderBy(heard => heard.Position)
            .ToArray();
        Assert.True(uiProgress.Length >= 2);
        Assert.True(uiProgress[0].Position < uiProgress[1].Position);
        Assert.Equal("pipeline", Assert.IsType<ResearchJobProgress>(uiProgress[0].Body).Stage);
        Assert.Equal("mentions", Assert.IsType<ResearchJobProgress>(uiProgress[1].Body).Stage);

        var uiStarted = uiReading.HeardSingle<ResearchJobStarted>();
        var uiCompleted = uiReading.HeardSingle<ResearchJobCompleted>();
        Assert.True(uiStarted.Position < uiProgress[0].Position);
        Assert.True(uiProgress[1].Position < uiCompleted.Position);

        Assert.True(startedSaid.Position < progress[0].Position);
        Assert.True(progress[1].Position < completedSaid.Position);
    }
}
