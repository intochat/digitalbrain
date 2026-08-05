using DigitalBrain.Mocks;
using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class ElonXPostCryptoDashboardTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<TopicRouter>()
            .AddModule<MarketSignalLedger>()
            .AddModule<CryptoMarket>()
            .AddModule<CryptoDashboard>()
            .AddModule<ChartRenderer>();

    [Fact(DisplayName = "Elon X post → six-coin crypto dashboard")]
    public async Task XPostObservedBecomesChartPointsWithAnnotation()
    {
        var ct = Cancellation;
        var seriesId = "owner-six";
        var session = Brain.Session(seriesId);
        var routerId = new NeuronId("topicrouter", seriesId);
        var marketId = new NeuronId("cryptomarket", seriesId);
        var dashboardId = new NeuronId("cryptodashboard", seriesId);
        var rendererId = new NeuronId("chartrenderer", seriesId);
        var postId = "x-elon-42";
        var text = "BTC and ETH just ripped — markets are awake.";
        var postAt = new DateTimeOffset(2026, 8, 5, 14, 30, 0, TimeSpan.Zero);

        await session.EmitAsync(new XPostObserved(postId, "elonmusk", text, postAt), ct);

        var dashboardReading = await WaitForJournalAsync(
            dashboardId,
            reading => reading.AllSaid<ChartPointAppended>().Count == 2
                && reading.AllSaid<ChartAnnotationAdded>().Count == 1,
            "two ChartPointAppended and one ChartAnnotationAdded",
            ct);

        var points = dashboardReading.AllSaid<ChartPointAppended>()
            .Select(said => Assert.IsType<ChartPointAppended>(said.Body))
            .OrderBy(point => point.Symbol, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(2, points.Length);
        Assert.Equal("BTC", points[0].Symbol);
        Assert.Equal(98_123.5, points[0].Price);
        Assert.Equal(1.2, points[0].Delta);
        Assert.Equal(seriesId, points[0].SeriesId);
        Assert.Equal(postId, points[0].PostId);
        Assert.Equal(postAt, points[0].At);
        Assert.Equal("ETH", points[1].Symbol);
        Assert.Equal(3_450.0, points[1].Price);
        Assert.Equal(-0.4, points[1].Delta);
        Assert.Equal(seriesId, points[1].SeriesId);
        Assert.Equal(postId, points[1].PostId);

        var annotationSaid = dashboardReading.SaidSingle<ChartAnnotationAdded>();
        var annotation = Assert.IsType<ChartAnnotationAdded>(annotationSaid.Body);
        Assert.Equal(postId, annotation.PostId);
        Assert.Equal(text, annotation.Excerpt);
        Assert.Equal(["BTC", "ETH"], annotation.LinkedSymbols);
        Assert.Contains("BTC", annotation.Description, StringComparison.Ordinal);
        Assert.Contains("ETH", annotation.Description, StringComparison.Ordinal);
        Assert.Contains(text, annotation.Description, StringComparison.Ordinal);

        var sessionReading = await ReadAsync(session.Id, ct);
        var observedSaid = sessionReading.SaidSingle<XPostObserved>();
        Assert.Equal("declared", observedSaid.DeliveryTo(routerId).Via);

        var routerReading = await ReadAsync(routerId, ct);
        var observedHeard = routerReading.HeardSingle<XPostObserved>();
        Assert.Equal(session.Id, observedHeard.Metadata.Source);
        Assert.Equal(observedSaid.Position, observedHeard.Metadata.Sequence);

        var classifiedSaid = routerReading.SaidSingle<MarketSignalClassified>();
        var classified = Assert.IsType<MarketSignalClassified>(classifiedSaid.Body);
        Assert.Equal(postId, classified.PostId);
        Assert.Equal(["BTC", "ETH"], classified.AssetHints);
        Assert.Equal(1.0, classified.Relevance);

        var annotateSaid = routerReading.SaidSingle<DashboardAnnotateAsked>();
        Assert.Equal("declared", annotateSaid.DeliveryTo(dashboardId).Via);
        Assert.Equal(new SynapseRef(session.Id, observedSaid.Position), annotateSaid.Cause);

        var annotateHeard = dashboardReading.HeardSingle<DashboardAnnotateAsked>();
        Assert.Equal(routerId, annotateHeard.Metadata.Source);
        Assert.Equal(annotateSaid.Position, annotateHeard.Metadata.Sequence);

        var spotAsked = dashboardReading.SaidSingle<SpotSnapshotAsked>();
        Assert.Equal("ask", spotAsked.DeliveryTo(marketId).Via);
        Assert.Equal(["BTC", "ETH"], Assert.IsType<SpotSnapshotAsked>(spotAsked.Body).Symbols);

        var marketReading = await ReadAsync(marketId, ct);
        var spotHeard = marketReading.HeardSingle<SpotSnapshotAsked>();
        Assert.Equal(dashboardId, spotHeard.Metadata.Source);
        Assert.Equal(spotAsked.Position, spotHeard.Metadata.Sequence);

        var spotAnswered = marketReading.SaidSingle<SpotSnapshotAnswered>();
        Assert.Equal(new SynapseRef(dashboardId, spotAsked.Position), spotAnswered.Answers);
        // Declared continuation on the asker can win the Via stamp; Answers is the ask match.
        Assert.NotNull(spotAnswered.DeliveryToOrNull(dashboardId));

        var snapshotHeard = dashboardReading.HeardSingle<SpotSnapshotAnswered>();
        Assert.Equal(marketId, snapshotHeard.Metadata.Source);
        Assert.Equal(new SynapseRef(dashboardId, spotAsked.Position), snapshotHeard.Answers);

        foreach (var pointSaid in dashboardReading.AllSaid<ChartPointAppended>())
        {
            Assert.Equal(new SynapseRef(marketId, snapshotHeard.Metadata.Sequence), pointSaid.Cause);
            Assert.Equal("declared", pointSaid.DeliveryTo(rendererId).Via);
        }

        Assert.Equal(
            new SynapseRef(marketId, snapshotHeard.Metadata.Sequence),
            annotationSaid.Cause);
        Assert.Equal("declared", annotationSaid.DeliveryTo(rendererId).Via);

        var rendererReading = await WaitForJournalAsync(
            rendererId,
            reading => reading.AllHeard<ChartPointAppended>().Count == 2
                && reading.AllHeard<ChartAnnotationAdded>().Count == 1,
            "two heard ChartPointAppended and one ChartAnnotationAdded",
            ct);
        Assert.Equal(2, rendererReading.AllHeard<ChartPointAppended>().Count);
        Assert.Equal(postId, Assert.IsType<ChartAnnotationAdded>(
            rendererReading.HeardSingle<ChartAnnotationAdded>().Body).PostId);
    }
}
