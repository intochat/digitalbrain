using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class LiveDashboardStreamSubscriptionTests(BrainTestClusters clusters)
    : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<RevenueMetricsProjector>()
            .AddModule<UiEdgeDashboard>();

    [Fact(DisplayName =
        "Live dashboard: subscription snapshot then OpportunityClosedWon/InvoicePaid/PO email revise KPI tiles + chart points")]
    public async Task AmbientDomainFactsDriveLiveKpiAndChart()
    {
        var ct = Cancellation;
        var context = "revenue-pulse";
        var session = Brain.Session(context);
        var metricsId = new NeuronId("revenuemetricsprojector", context);
        var edgeId = new NeuronId("uiedgedashboard", context);
        var sceneId = "revenue-pulse";

        await session.EmitAsync(
            new DashboardSubscriptionAttached(sceneId, MetricsNeuronKind: "revenuemetricsprojector"),
            ct);

        var afterAttach = await WaitForJournalAsync(
            metricsId,
            reading => reading.AllSaid<DashboardSnapshot>().Count == 1,
            "metrics said DashboardSnapshot on attach",
            ct);

        var edgeSnap = await WaitForJournalAsync(
            edgeId,
            reading => reading.AllHeard<DashboardSnapshot>().Count == 1,
            "UI edge heard DashboardSnapshot",
            ct);

        var snapSaid = afterAttach.SaidSingle<DashboardSnapshot>();
        Assert.Equal("declared", snapSaid.DeliveryTo(edgeId).Via);
        var snap = Assert.IsType<DashboardSnapshot>(snapSaid.Body);
        Assert.Equal(sceneId, snap.SceneId);
        Assert.Equal(0, snap.ClosedWonToday);
        Assert.Equal(metricsId, edgeSnap.HeardSingle<DashboardSnapshot>().Metadata.Source);

        await session.EmitAsync(new OpportunityClosedWon("opp-1", "Acme", Amount: 12_000), ct);
        await session.EmitAsync(new InvoicePaid("inv-9", Amount: 4_500, Currency: "USD"), ct);
        await session.EmitAsync(
            new PurchaseOrderEmailDetected("msg-po-1", Vendor: "Contoso Parts", AmountHint: 2_000),
            ct);

        var metricsDone = await WaitForJournalAsync(
            metricsId,
            reading => reading.AllSaid<KpiTileUpdated>().Count == 3
                && reading.AllSaid<RevenueChartPointAppended>().Count == 2
                && reading.AllHeard<OpportunityClosedWon>().Count == 1
                && reading.AllHeard<InvoicePaid>().Count == 1
                && reading.AllHeard<PurchaseOrderEmailDetected>().Count == 1,
            "three KPI tiles and two chart points after domain facts",
            ct);

        var edgeDone = await WaitForJournalAsync(
            edgeId,
            reading => reading.AllHeard<KpiTileUpdated>().Count == 3
                && reading.AllHeard<RevenueChartPointAppended>().Count == 2,
            "UI edge heard all tile/chart revisions",
            ct);

        var tiles = metricsDone.AllSaid<KpiTileUpdated>()
            .Select(said => Assert.IsType<KpiTileUpdated>(said.Body))
            .ToDictionary(tile => tile.Tile, StringComparer.Ordinal);

        Assert.Equal(12_000, tiles["closedWonToday"].Value);
        Assert.Equal(4_500, tiles["cashIn"].Value);
        Assert.Equal(1, tiles["openPOs"].Value);
        Assert.Equal(4, tiles["openPOs"].Revision);

        var points = metricsDone.AllSaid<RevenueChartPointAppended>()
            .Select(said => Assert.IsType<RevenueChartPointAppended>(said.Body))
            .ToArray();
        Assert.Contains(points, p => p is { Series: "revenue", Amount: 12_000 });
        Assert.Contains(points, p => p is { Series: "cash", Amount: 4_500 });

        var sessionReading = await ReadAsync(session.Id, ct);
        var wonSaid = sessionReading.SaidSingle<OpportunityClosedWon>();
        Assert.Equal("declared", wonSaid.DeliveryTo(metricsId).Via);

        var closedTile = metricsDone.AllSaid<KpiTileUpdated>()
            .Single(said => Assert.IsType<KpiTileUpdated>(said.Body).Tile == "closedWonToday");
        Assert.Equal(new SynapseRef(session.Id, wonSaid.Position), closedTile.Cause);
        Assert.Equal("declared", closedTile.DeliveryTo(edgeId).Via);

        Assert.Equal(3, edgeDone.AllHeard<KpiTileUpdated>().Count);
        Assert.Equal(2, edgeDone.AllHeard<RevenueChartPointAppended>().Count);
    }
}
