using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class RichChatImageSalesChartTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<RichChatDesk>()
            .AddModule<MockVisionExtract>()
            .AddModule<MockOpportunityStageStats>()
            .AddModule<RichChatShellLedger>();

    [Fact(DisplayName =
        "Rich chat image → sales chart: attachment + vision ask + SF stage stats → ChartSpec + FunnelTable + caption; Cause chain holds")]
    public async Task ImageTurnFusesVisionCrmAndChartArtifacts()
    {
        var ct = Cancellation;
        var context = "sales-desk-chart";
        var session = Brain.Session(context);
        var deskId = new NeuronId("richchatdesk", context);
        var visionId = new NeuronId("mockvisionextract", context);
        var statsId = new NeuronId("mockopportunitystagestats", context);
        var shellId = new NeuronId("richchatshellledger", context);
        var blob = "blob://whiteboard/funnel-sketch.png";

        await session.EmitAsync(
            new RichChatUserMessaged("Turn this whiteboard into a funnel with live SF counts", blob),
            ct);

        var deskReading = await WaitForJournalAsync(
            deskId,
            reading => reading.AllSaid<ChatAttachmentAdded>().Count == 1
                && reading.AllSaid<VisionExtractAsked>().Count == 1
                && reading.AllHeard<VisionExtractAnswered>().Count == 1
                && reading.AllSaid<OpportunityStageStatsAsked>().Count == 1
                && reading.AllHeard<OpportunityStageStatsAnswered>().Count == 1
                && reading.AllSaid<ChartSpec>().Count == 1
                && reading.AllSaid<FunnelTableProduced>().Count == 1
                && reading.AllSaid<RichChatAssistantSaid>().Count == 1,
            "desk completed vision→stats→chart chain",
            ct);

        var shellReading = await WaitForJournalAsync(
            shellId,
            reading => reading.AllHeard<ChatAttachmentAdded>().Count == 1
                && reading.AllHeard<ChartSpec>().Count == 1
                && reading.AllHeard<FunnelTableProduced>().Count == 1
                && reading.AllHeard<RichChatAssistantSaid>().Count == 1,
            "shell heard attachment + chart + table + caption",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var userSaid = sessionReading.SaidSingle<RichChatUserMessaged>();
        Assert.Equal("declared", userSaid.DeliveryTo(deskId).Via);

        var attachSaid = deskReading.SaidSingle<ChatAttachmentAdded>();
        Assert.Equal(new SynapseRef(session.Id, userSaid.Position), attachSaid.Cause);
        Assert.Equal("declared", attachSaid.DeliveryTo(shellId).Via);
        Assert.Equal(blob, Assert.IsType<ChatAttachmentAdded>(attachSaid.Body).BlobRef);

        var visionAsked = deskReading.SaidSingle<VisionExtractAsked>();
        Assert.Equal("ask", visionAsked.DeliveryTo(visionId).Via);
        Assert.Equal(new SynapseRef(session.Id, userSaid.Position), visionAsked.Cause);

        var visionReading = await ReadAsync(visionId, ct);
        var visionAnswered = visionReading.SaidSingle<VisionExtractAnswered>();
        Assert.Equal(new SynapseRef(deskId, visionAsked.Position), visionAnswered.Answers);
        Assert.Equal(0.91, Assert.IsType<VisionExtractAnswered>(visionAnswered.Body).Confidence);

        var statsAsked = deskReading.SaidSingle<OpportunityStageStatsAsked>();
        Assert.Equal("ask", statsAsked.DeliveryTo(statsId).Via);
        Assert.Equal(new SynapseRef(visionId, visionAnswered.Position), statsAsked.Cause);

        var chartSaid = deskReading.SaidSingle<ChartSpec>();
        Assert.Equal("declared", chartSaid.DeliveryTo(shellId).Via);
        var chart = Assert.IsType<ChartSpec>(chartSaid.Body);
        Assert.Equal("sales-funnel", chart.ChartId);
        Assert.Contains(chart.Series, s => s.StartsWith("Prospect:", StringComparison.Ordinal));
        Assert.Contains(chart.Series, s => s.StartsWith("ClosedWon:", StringComparison.Ordinal));

        var tableSaid = deskReading.SaidSingle<FunnelTableProduced>();
        Assert.Equal(chartSaid.Cause, tableSaid.Cause);
        Assert.Equal(3, Assert.IsType<FunnelTableProduced>(tableSaid.Body).Rows.Length);

        var captionSaid = deskReading.SaidSingle<RichChatAssistantSaid>();
        Assert.Contains(
            "Salesforce",
            Assert.IsType<RichChatAssistantSaid>(captionSaid.Body).Caption,
            StringComparison.Ordinal);

        Assert.Equal(deskId, shellReading.HeardSingle<ChartSpec>().Metadata.Source);
        Assert.Equal(chartSaid.Position, shellReading.HeardSingle<ChartSpec>().Metadata.Sequence);
    }
}
