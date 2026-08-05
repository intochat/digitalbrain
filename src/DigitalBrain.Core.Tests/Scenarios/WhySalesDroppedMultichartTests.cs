using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class WhySalesDroppedMultichartTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain.AddModule<SalesDiagnostic>().AddModule<SalesDiagnosticUi>();

    [Fact(DisplayName =
        "Why sales dropped: seed MetricObserved first → SalesDropAsked → ≥2 ChartSpec + WhySalesAnswer cites journaled metrics")]
    public async Task WhySalesAnswerCitesPriorJournaledMetricsWithMultiChart()
    {
        var ct = Cancellation;
        var context = "sales-month-7";
        var session = Brain.Session(context);
        var diagnosticId = new NeuronId("salesdiagnostic", context);
        var uiId = new NeuronId("salesdiagnosticui", context);
        var period = "2026-07";

        var revenue = new MetricObserved("m-rev", "revenue", 1.2e6, "USD");
        var tickets = new MetricObserved("m-tix", "support-tickets", 840, "count");
        var winRate = new MetricObserved("m-win", "win-rate", 0.31, "ratio");

        await session.EmitAsync(revenue, ct);
        await session.EmitAsync(tickets, ct);
        await session.EmitAsync(winRate, ct);

        var seeded = await WaitForJournalAsync(
            diagnosticId,
            reading => reading.AllHeard<MetricObserved>().Count == 3,
            "three MetricObserved heard on sales diagnostic",
            ct);

        var journaledMetrics = seeded.AllHeard<MetricObserved>()
            .Select(heard => Assert.IsType<MetricObserved>(heard.Body))
            .OrderBy(metric => metric.MetricId, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(3, journaledMetrics.Length);
        Assert.Equal(["m-rev", "m-tix", "m-win"], [.. journaledMetrics.Select(m => m.MetricId)]);

        // S04 pattern: instruction/metrics journaled before the question that cites them.
        var metricPositions = seeded.AllHeard<MetricObserved>().Select(heard => heard.Position).ToArray();
        Assert.Equal(3, metricPositions.Length);

        await session.EmitAsync(new SalesDropAsked(period), ct);

        var diagnosticAfter = await WaitForJournalAsync(
            diagnosticId,
            reading => reading.AllSaid<ChartSpec>().Count >= 2
                && reading.AllSaid<WhySalesAnswer>().Count == 1
                && reading.AllHeard<SalesDropAsked>().Count == 1,
            "≥2 ChartSpec and WhySalesAnswer said after SalesDropAsked",
            ct);

        var uiReading = await WaitForJournalAsync(
            uiId,
            reading => reading.AllHeard<ChartSpec>().Count >= 2
                && reading.AllHeard<WhySalesAnswer>().Count == 1,
            "UI heard ≥2 ChartSpec and WhySalesAnswer",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var askSaid = sessionReading.SaidSingle<SalesDropAsked>();
        Assert.Equal("declared", askSaid.DeliveryTo(diagnosticId).Via);

        var askHeard = diagnosticAfter.HeardSingle<SalesDropAsked>();
        Assert.Equal(session.Id, askHeard.Metadata.Source);
        Assert.Equal(askSaid.Position, askHeard.Metadata.Sequence);

        var chartsSaid = diagnosticAfter.AllSaid<ChartSpec>();
        Assert.True(chartsSaid.Count >= 2, $"expected ≥2 ChartSpec, got {chartsSaid.Count}");
        Assert.Equal(3, chartsSaid.Count);
        foreach (var chartSaid in chartsSaid)
        {
            Assert.Equal(new SynapseRef(session.Id, askSaid.Position), chartSaid.Cause);
            Assert.Equal("declared", chartSaid.DeliveryTo(uiId).Via);
            Assert.True(
                metricPositions.All(position => position < chartSaid.Position),
                "every MetricObserved must be journaled before ChartSpec on the diagnostic");
        }

        var chartBodies = chartsSaid
            .Select(said => Assert.IsType<ChartSpec>(said.Body))
            .OrderBy(chart => chart.ChartId, StringComparer.Ordinal)
            .ToArray();
        Assert.Contains(chartBodies, chart => chart.ChartId.Contains("revenue", StringComparison.Ordinal));
        Assert.Contains(chartBodies, chart => chart.ChartId.Contains("support-tickets", StringComparison.Ordinal));
        Assert.Contains(chartBodies, chart => chart.ChartId.Contains("win-rate", StringComparison.Ordinal));

        var whySaid = diagnosticAfter.SaidSingle<WhySalesAnswer>();
        Assert.Equal(new SynapseRef(session.Id, askSaid.Position), whySaid.Cause);
        Assert.Equal("declared", whySaid.DeliveryTo(uiId).Via);
        Assert.True(
            metricPositions.All(position => position < whySaid.Position),
            "MetricObserved positions must precede WhySalesAnswer");

        var why = Assert.IsType<WhySalesAnswer>(whySaid.Body);
        Assert.Equal(period, why.Period);
        Assert.Equal(3, why.CitedMetricIds.Length);
        Assert.Equal(["m-rev", "m-tix", "m-win"], [.. why.CitedMetricIds.OrderBy(id => id, StringComparer.Ordinal)]);
        Assert.Equal(["revenue", "support-tickets", "win-rate"], [.. why.CitedSeries.OrderBy(s => s, StringComparer.Ordinal)]);

        // Re-read journals: answer cites exactly the earlier journaled MetricObserved bodies (S04).
        var reseeded = (await ReadAsync(diagnosticId, ct)).AllHeard<MetricObserved>()
            .Select(heard => Assert.IsType<MetricObserved>(heard.Body))
            .ToDictionary(metric => metric.MetricId, StringComparer.Ordinal);
        foreach (var citedId in why.CitedMetricIds)
        {
            Assert.True(reseeded.ContainsKey(citedId), $"WhySalesAnswer cited unknown metric id {citedId}");
            Assert.Contains(citedId, why.Narrative, StringComparison.Ordinal);
            Assert.Contains(reseeded[citedId].Series, why.Narrative, StringComparison.Ordinal);
        }

        Assert.Equal(3, uiReading.AllHeard<ChartSpec>().Count);
        Assert.Equal(diagnosticId, uiReading.HeardSingle<WhySalesAnswer>().Metadata.Source);
        Assert.Equal(whySaid.Position, uiReading.HeardSingle<WhySalesAnswer>().Metadata.Sequence);
    }
}
