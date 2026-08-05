using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class StatelessWorkerEmbeddingsTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<NotesIndexer>()
            .AddModule<NotesIndexLedger>();

    [Fact(DisplayName =
        "Stateless worker embeddings (Stage-1 honest: pure service in turn, not Orleans worker): NotesImportStarted → EmbeddingBatchDone×N → NotesIndexReady")]
    public async Task ChunkedEmbedServiceJournalsBatchProgressAndReady()
    {
        var ct = Cancellation;
        var context = "import-3";
        var session = Brain.Session(context);
        var indexerId = new NeuronId("notesindexer", context);
        var ledgerId = new NeuronId("notesindexledger", context);
        // Small count keeps the proof meaningful without 10k journal noise; same chunk loop.
        var noteCount = 20;
        var chunkSize = 5;
        var expectedBatches = noteCount / chunkSize;

        await session.EmitAsync(
            new NotesImportStarted("import-3", noteCount, chunkSize),
            ct);

        var indexerReading = await WaitForJournalAsync(
            indexerId,
            reading => reading.AllSaid<NotesIndexReady>().Count == 1
                && reading.AllSaid<EmbeddingBatchDone>().Count == expectedBatches
                && reading.AllSaid<NotesIndexProgress>().Count == expectedBatches,
            "all batches journaled + NotesIndexReady",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var startSaid = sessionReading.SaidSingle<NotesImportStarted>();
        Assert.Equal("declared", startSaid.DeliveryTo(indexerId).Via);

        var batches = indexerReading.AllSaid<EmbeddingBatchDone>();
        Assert.Equal(expectedBatches, batches.Count);
        Assert.All(batches, said => Assert.Equal("declared", said.DeliveryTo(ledgerId).Via));
        Assert.Equal(0, Assert.IsType<EmbeddingBatchDone>(batches[0].Body).FromInclusive);
        Assert.Equal(chunkSize, Assert.IsType<EmbeddingBatchDone>(batches[0].Body).EmbeddedCount);
        Assert.Equal(
            noteCount,
            batches.Sum(s => Assert.IsType<EmbeddingBatchDone>(s.Body).EmbeddedCount));

        var readySaid = indexerReading.SaidSingle<NotesIndexReady>();
        Assert.Equal(new SynapseRef(session.Id, startSaid.Position), readySaid.Cause);
        Assert.Equal(noteCount, Assert.IsType<NotesIndexReady>(readySaid.Body).TotalEmbedded);

        var progress = indexerReading.AllSaid<NotesIndexProgress>();
        Assert.Equal(noteCount, Assert.IsType<NotesIndexProgress>(progress[^1].Body).Done);

        var ledgerReading = await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<EmbeddingBatchDone>().Count == expectedBatches
                && reading.AllHeard<NotesIndexReady>().Count == 1,
            "ledger heard batches + ready",
            ct);
        Assert.Equal(indexerId, ledgerReading.HeardSingle<NotesIndexReady>().Metadata.Source);
    }
}
