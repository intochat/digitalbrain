using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class WhiteboardPhotoTasksTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<WhiteboardVisionIngress>()
            .AddModule<WhiteboardOcrWorker>()
            .AddModule<WhiteboardTaskStore>()
            .AddModule<WhiteboardUiLedger>();

    [Fact(DisplayName =
        "Whiteboard photo tasks: ImageAttached → OcrTextReady → WhiteboardTasksProposed → Confirm → WhiteboardTaskCreated×N")]
    public async Task ImageToOcrToProposedToConfirmedTasks()
    {
        var ct = Cancellation;
        var context = "board-capture";
        var session = Brain.Session(context);
        var ingressId = new NeuronId("whiteboardvisioningress", context);
        var ocrId = new NeuronId("whiteboardocrworker", context);
        var storeId = new NeuronId("whiteboardtaskstore", context);
        var ledgerId = new NeuronId("whiteboarduiledger", context);
        var blob = "blob-wb-1";

        await session.EmitAsync(new WhiteboardImageAttached(blob, "image/jpeg"), ct);

        var ingressReading = await WaitForJournalAsync(
            ingressId,
            reading => reading.AllSaid<WhiteboardTasksProposed>().Count == 1
                && reading.AllSaid<WhiteboardParsed>().Count == 1
                && reading.AllHeard<OcrTextReady>().Count == 1,
            "OCR answered and tasks proposed",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var attachedSaid = sessionReading.SaidSingle<WhiteboardImageAttached>();
        Assert.Equal("declared", attachedSaid.DeliveryTo(ingressId).Via);

        var ocrAsk = ingressReading.SaidSingle<RunOcrAsked>();
        Assert.Equal("ask", ocrAsk.DeliveryTo(ocrId).Via);
        Assert.Equal(new SynapseRef(session.Id, attachedSaid.Position), ocrAsk.Cause);

        var ocrHeard = ingressReading.HeardSingle<OcrTextReady>();
        Assert.Equal(ocrId, ocrHeard.Metadata.Source);
        Assert.Equal(new SynapseRef(ingressId, ocrAsk.Position), ocrHeard.Answers);

        var proposedSaid = ingressReading.SaidSingle<WhiteboardTasksProposed>();
        Assert.Equal("declared", proposedSaid.DeliveryTo(storeId).Via);
        var proposed = Assert.IsType<WhiteboardTasksProposed>(proposedSaid.Body);
        Assert.Equal(3, proposed.Titles.Length);
        Assert.Contains(proposed.Titles, t => t.Contains("Priya", StringComparison.Ordinal));

        await WaitForJournalAsync(
            storeId,
            reading => reading.AllHeard<WhiteboardTasksProposed>().Count == 1,
            "store heard proposals",
            ct);

        await session.EmitAsync(new WhiteboardConfirmTasks(blob), ct);

        var storeReading = await WaitForJournalAsync(
            storeId,
            reading => reading.AllSaid<WhiteboardTaskCreated>().Count == 3,
            "confirm created three tasks",
            ct);

        var created = storeReading.AllSaid<WhiteboardTaskCreated>();
        Assert.All(created, said => Assert.Equal("declared", said.DeliveryTo(ledgerId).Via));
        Assert.Equal(3, created.Select(s => Assert.IsType<WhiteboardTaskCreated>(s.Body).Title).Distinct().Count());

        var ledgerReading = await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<WhiteboardTaskCreated>().Count == 3
                && reading.AllHeard<WhiteboardParsed>().Count == 1,
            "UI ledger heard parse + tasks",
            ct);
        Assert.Equal(storeId, ledgerReading.AllHeard<WhiteboardTaskCreated>()[0].Metadata.Source);
    }
}
