using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class RichMultimodalAssistantResponseTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<MultimodalAssistant>()
            .AddModule<ShellMultimodalLedger>();

    [Fact(DisplayName =
        "Rich multimodal assistant response: one MultimodalUserAsked turn journals AssistantText + ChartSpec + ImageRef + ButtonOffer; shell ledger hears all four")]
    public async Task OneUserTurnJournalsFourMultimodalFactTypes()
    {
        var ct = Cancellation;
        var context = "money-desk";
        var session = Brain.Session(context);
        var assistantId = new NeuronId("multimodalassistant", context);
        var shellId = new NeuronId("shellmultimodalledger", context);
        var prompt = "portfolio health check";

        await session.EmitAsync(new MultimodalUserAsked(prompt), ct);

        var assistantReading = await WaitForJournalAsync(
            assistantId,
            reading => reading.AllSaid<AssistantText>().Count == 1
                && reading.AllSaid<ChartSpec>().Count == 1
                && reading.AllSaid<ImageRef>().Count == 1
                && reading.AllSaid<ButtonOffer>().Count == 1,
            "assistant said AssistantText, ChartSpec, ImageRef, ButtonOffer",
            ct);

        var shellReading = await WaitForJournalAsync(
            shellId,
            reading => reading.AllHeard<AssistantText>().Count == 1
                && reading.AllHeard<ChartSpec>().Count == 1
                && reading.AllHeard<ImageRef>().Count == 1
                && reading.AllHeard<ButtonOffer>().Count == 1,
            "shell ledger heard all four multimodal fact types",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var userSaid = sessionReading.SaidSingle<MultimodalUserAsked>();
        Assert.Equal("declared", userSaid.DeliveryTo(assistantId).Via);
        Assert.Equal(prompt, Assert.IsType<MultimodalUserAsked>(userSaid.Body).Text);

        var userHeard = assistantReading.HeardSingle<MultimodalUserAsked>();
        Assert.Equal(session.Id, userHeard.Metadata.Source);
        Assert.Equal(userSaid.Position, userHeard.Metadata.Sequence);

        var userCause = new SynapseRef(session.Id, userSaid.Position);

        var textSaid = assistantReading.SaidSingle<AssistantText>();
        Assert.Equal(userCause, textSaid.Cause);
        Assert.Equal("declared", textSaid.DeliveryTo(shellId).Via);
        Assert.Contains("Portfolio health", Assert.IsType<AssistantText>(textSaid.Body).Text, StringComparison.Ordinal);

        var chartSaid = assistantReading.SaidSingle<ChartSpec>();
        Assert.Equal(userCause, chartSaid.Cause);
        Assert.Equal("declared", chartSaid.DeliveryTo(shellId).Via);
        var chart = Assert.IsType<ChartSpec>(chartSaid.Body);
        Assert.Equal("portfolio-30d", chart.ChartId);
        Assert.Equal(["equity", "benchmark"], chart.Series);

        var imageSaid = assistantReading.SaidSingle<ImageRef>();
        Assert.Equal(userCause, imageSaid.Cause);
        Assert.Equal("declared", imageSaid.DeliveryTo(shellId).Via);
        var image = Assert.IsType<ImageRef>(imageSaid.Body);
        Assert.Equal("blob://charts/portfolio-30d.png", image.BlobRef);
        Assert.Equal("image/png", image.MimeType);

        var buttonSaid = assistantReading.SaidSingle<ButtonOffer>();
        Assert.Equal(userCause, buttonSaid.Cause);
        Assert.Equal("declared", buttonSaid.DeliveryTo(shellId).Via);
        var button = Assert.IsType<ButtonOffer>(buttonSaid.Body);
        Assert.Equal("propose-rebalance", button.ActionId);
        Assert.Equal("Rebalance proposal", button.Label);

        // Four distinct fact kinds from one turn — same Cause, separate journal rows.
        Assert.Equal(4, new[]
        {
            textSaid.Kind,
            chartSaid.Kind,
            imageSaid.Kind,
            buttonSaid.Kind,
        }.Distinct(StringComparer.Ordinal).Count());

        Assert.Equal(assistantId, shellReading.HeardSingle<AssistantText>().Metadata.Source);
        Assert.Equal(assistantId, shellReading.HeardSingle<ChartSpec>().Metadata.Source);
        Assert.Equal(assistantId, shellReading.HeardSingle<ImageRef>().Metadata.Source);
        Assert.Equal(assistantId, shellReading.HeardSingle<ButtonOffer>().Metadata.Source);
        Assert.Equal(textSaid.Position, shellReading.HeardSingle<AssistantText>().Metadata.Sequence);
        Assert.Equal(chartSaid.Position, shellReading.HeardSingle<ChartSpec>().Metadata.Sequence);
        Assert.Equal(imageSaid.Position, shellReading.HeardSingle<ImageRef>().Metadata.Sequence);
        Assert.Equal(buttonSaid.Position, shellReading.HeardSingle<ButtonOffer>().Metadata.Sequence);
    }
}
