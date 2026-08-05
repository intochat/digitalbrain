namespace DigitalBrain.Core.Tests.Scenarios;

// Stage-1 honest: no Orleans stateless worker in Core — pure service called inside the turn;
// progress is journaled as EmbeddingBatchDone facts.

public sealed record NotesImportStarted(string ImportId, int NoteCount, int ChunkSize) : Synapse;

public sealed record EmbeddingBatchDone(
    string ImportId,
    int ChunkIndex,
    int FromInclusive,
    int ToExclusive,
    int EmbeddedCount) : Synapse;

public sealed record NotesIndexProgress(string ImportId, int Done, int Total) : Synapse;

public sealed record NotesIndexReady(string ImportId, int TotalEmbedded) : Synapse;

// Pure (non-Neuron) embed service — called inside the orchestrator turn.
public static class FakeEmbeddingService
{
    public static int EmbedChunk(IReadOnlyList<string> texts)
        => texts.Count;
}

public sealed class NotesIndexer : Neuron<NotesIndexerState>, INeuron<NotesImportStarted>
{
    public Task HandleAsync(NotesImportStarted fact, CancellationToken cancellationToken)
    {
        var chunkSize = Math.Max(1, fact.ChunkSize);
        var total = fact.NoteCount;
        var done = 0;
        var chunkIndex = 0;

        while (done < total)
        {
            var take = Math.Min(chunkSize, total - done);
            var texts = Enumerable.Range(done, take).Select(i => $"note-{i}").ToArray();
            var embedded = FakeEmbeddingService.EmbedChunk(texts);
            Emit(new EmbeddingBatchDone(
                fact.ImportId,
                chunkIndex,
                FromInclusive: done,
                ToExclusive: done + take,
                EmbeddedCount: embedded));
            done += take;
            chunkIndex++;
            Emit(new NotesIndexProgress(fact.ImportId, done, total));
        }

        State.TotalEmbedded = done;
        Emit(new NotesIndexReady(fact.ImportId, done));
        return Task.CompletedTask;
    }
}

public sealed class NotesIndexerState
{
    public int TotalEmbedded { get; set; }
}

public sealed class NotesIndexLedger : Neuron,
    INeuron<EmbeddingBatchDone>,
    INeuron<NotesIndexProgress>,
    INeuron<NotesIndexReady>
{
    public Task HandleAsync(EmbeddingBatchDone fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(NotesIndexProgress fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(NotesIndexReady fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
