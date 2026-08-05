namespace DigitalBrain;

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

    internal static void Write(DeliveryEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        RequestContext.Set(SourceKindKey, envelope.Source.Kind);
        RequestContext.Set(SourceNameKey, envelope.Source.Name);
        RequestContext.Set(SequenceKey, envelope.Sequence);
        RequestContext.Set(TimestampKey, envelope.Timestamp.UtcTicks);
        WriteRef(CauseKindKey, CauseNameKey, CauseSequenceKey, envelope.Cause);
        WriteRef(AnswersKindKey, AnswersNameKey, AnswersSequenceKey, envelope.Answers);
    }

    internal static DeliveryEnvelope? Consume()
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

        return new DeliveryEnvelope(
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
