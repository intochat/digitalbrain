namespace DigitalBrain;

// The envelope over the wire (§4): kind strings, longs and ticks in RequestContext —
// transport convenience only, never AQN, never authority (the journal is). The outgoing
// filter writes from the sender's staged delivery just before every wire call; the
// incoming filter consumes on arrival. Consume removes the keys and returns null when no
// envelope rode the call — the filter decides whether that is legal (it is not, for a
// delivery: a delivery without an envelope is a kernel bug).
internal static class SynapseHeaders
{
    private const string SourceKindKey = "db.src.kind";
    private const string SourceNameKey = "db.src.name";
    private const string SequenceKey = "db.seq";
    private const string TimestampKey = "db.at";
    private const string CauseKindKey = "db.cause.kind";
    private const string CauseNameKey = "db.cause.name";
    private const string CauseSequenceKey = "db.cause.seq";
    private const string AnswersKindKey = "db.answers.kind";
    private const string AnswersNameKey = "db.answers.name";
    private const string AnswersSequenceKey = "db.answers.seq";

    internal static void Write(SynapseMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        RequestContext.Set(SourceKindKey, metadata.Source.Kind);
        RequestContext.Set(SourceNameKey, metadata.Source.Name);
        RequestContext.Set(SequenceKey, metadata.Sequence);
        RequestContext.Set(TimestampKey, metadata.Timestamp.UtcTicks);
        WriteRef(CauseKindKey, CauseNameKey, CauseSequenceKey, metadata.Cause);
        WriteRef(AnswersKindKey, AnswersNameKey, AnswersSequenceKey, metadata.Answers);
    }

    internal static SynapseMetadata? Consume()
    {
        var sourceKind = TakeString(SourceKindKey);
        var sourceName = TakeString(SourceNameKey);
        var sequence = TakeInt64(SequenceKey);
        var timestampTicks = TakeInt64(TimestampKey);
        var cause = TakeRef(CauseKindKey, CauseNameKey, CauseSequenceKey);
        var answers = TakeRef(AnswersKindKey, AnswersNameKey, AnswersSequenceKey);

        if (sourceKind is null)
        {
            return null;
        }

        return new SynapseMetadata(
            new NeuronId(sourceKind, sourceName ?? throw Missing(SourceNameKey)),
            sequence ?? throw Missing(SequenceKey),
            new DateTimeOffset(timestampTicks ?? throw Missing(TimestampKey), TimeSpan.Zero),
            cause,
            answers);
    }

    private static void WriteRef(string kindKey, string nameKey, string sequenceKey, SynapseRef? reference)
    {
        if (reference is { } present)
        {
            RequestContext.Set(kindKey, present.Source.Kind);
            RequestContext.Set(nameKey, present.Source.Name);
            RequestContext.Set(sequenceKey, present.Sequence);
        }
        else
        {
            RequestContext.Remove(kindKey);
            RequestContext.Remove(nameKey);
            RequestContext.Remove(sequenceKey);
        }
    }

    private static SynapseRef? TakeRef(string kindKey, string nameKey, string sequenceKey)
    {
        var kind = TakeString(kindKey);
        var name = TakeString(nameKey);
        var sequence = TakeInt64(sequenceKey);

        return kind is null
            ? null
            : new SynapseRef(
                new NeuronId(kind, name ?? throw Missing(nameKey)),
                sequence ?? throw Missing(sequenceKey));
    }

    private static string? TakeString(string key)
    {
        var value = RequestContext.Get(key) as string;
        RequestContext.Remove(key);
        return value;
    }

    private static long? TakeInt64(string key)
    {
        var value = RequestContext.Get(key) is long present ? present : (long?)null;
        RequestContext.Remove(key);
        return value;
    }

    private static InvalidOperationException Missing(string key)
        => new($"The delivery envelope is torn: '{key}' is absent while its siblings arrived; "
            + "Core writes the whole envelope before every wire call.");
}
