namespace Ino.Core.Hosting;

/// <summary>
/// Lightweight, in-memory introspection sink populated by <c>SystemFirePort</c> on
/// every fire + broadcast. Backs the gateway's <c>GetJournal</c> / <c>GetMetrics</c>
/// RPCs and the Flutter inspector drawer. Retention is best-effort — entries fall
/// off a ring buffer once capacity is exceeded. Post-v0.1 this is replaced by a
/// persisted per-user journal; for now it's process-local to the system silo.
/// </summary>
public interface ISynapseJournal
{
    void Record(SynapseJournalEntry entry);

    IReadOnlyList<SynapseJournalEntry> Recent(string? neuronId, int limit);

    NeuronMetricsSnapshot Metrics(string? neuronId);
}

public sealed record SynapseJournalEntry(
    long TimestampUnixMs,
    string Kind,
    string SynapseVerb,
    string CorrelationId,
    string SourceNeuron,
    string TargetNeuron);

public sealed record NeuronMetricsSnapshot(
    IReadOnlyList<NeuronMetric> PerNeuron);

public sealed record NeuronMetric(
    string NeuronId,
    long FireCount,
    long BroadcastCount,
    long LastActivatedUnixMs);
