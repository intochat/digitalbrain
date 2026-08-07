namespace DigitalBrain;

internal sealed class RequestContextEnvelopeCarrier : IEnvelopeCarrier
{
    private const string SourceKind = "db.source.kind";
    private const string SourceName = "db.source.name";
    private const string Sequence = "db.sequence";
    private const string Timestamp = "db.timestamp";
    private const string Authority = "db.authority";
    private const string CauseKind = "db.cause.kind";
    private const string CauseName = "db.cause.name";
    private const string CauseSequence = "db.cause.sequence";

    public void Write(DeliveryEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        RequestContext.Set(SourceKind, envelope.Source.Kind);
        RequestContext.Set(SourceName, envelope.Source.Name);
        RequestContext.Set(Sequence, envelope.Sequence);
        RequestContext.Set(Timestamp, envelope.OccurredAt.UtcTicks);
        RequestContext.Set(Authority, (int)envelope.Authority);
        WriteReference(CauseKind, CauseName, CauseSequence, envelope.CausedBy);
    }

    public DeliveryEnvelope? Consume()
    {
        var kind = TakeString(SourceKind);
        var name = TakeString(SourceName);
        var sequence = TakeLong(Sequence);
        var timestamp = TakeLong(Timestamp);
        var authority = TakeInt(Authority);
        var cause = TakeReference(CauseKind, CauseName, CauseSequence);
        if (kind is null)
        {
            return null;
        }

        var source = new NeuronId(kind, name ?? throw Missing(SourceName));
        var recordedAuthority = authority is not null && Enum.IsDefined((SynapseOriginAuthority)authority.Value)
            ? (SynapseOriginAuthority)authority.Value
            : SynapseOriginAuthority.LegacyUnknown;
        return new DeliveryEnvelope(
            source,
            sequence ?? throw Missing(Sequence),
            new DateTimeOffset(timestamp ?? throw Missing(Timestamp), TimeSpan.Zero),
            SynapseSourceIdentity.ResolveLegacyAuthority(source, recordedAuthority),
            cause);
    }

    private static void WriteReference(
        string kindKey,
        string nameKey,
        string sequenceKey,
        SynapseReference? reference)
    {
        if (reference is { } value)
        {
            RequestContext.Set(kindKey, value.Source.Kind);
            RequestContext.Set(nameKey, value.Source.Name);
            RequestContext.Set(sequenceKey, value.Sequence);
            return;
        }

        RequestContext.Remove(kindKey);
        RequestContext.Remove(nameKey);
        RequestContext.Remove(sequenceKey);
    }

    private static SynapseReference? TakeReference(string kindKey, string nameKey, string sequenceKey)
    {
        var kind = TakeString(kindKey);
        var name = TakeString(nameKey);
        var sequence = TakeLong(sequenceKey);
        return kind is null
            ? null
            : new SynapseReference(
                new NeuronId(kind, name ?? throw Missing(nameKey)),
                sequence ?? throw Missing(sequenceKey));
    }

    private static string? TakeString(string key)
    {
        var value = RequestContext.Get(key) as string;
        RequestContext.Remove(key);
        return value;
    }

    private static long? TakeLong(string key)
    {
        var value = RequestContext.Get(key);
        RequestContext.Remove(key);
        return value switch
        {
            long number => number,
            int number => number,
            string text when long.TryParse(text, out var number) => number,
            _ => null,
        };
    }

    private static int? TakeInt(string key)
    {
        var value = RequestContext.Get(key);
        RequestContext.Remove(key);
        return value switch
        {
            int number => number,
            long number when number is >= int.MinValue and <= int.MaxValue => (int)number,
            string text when int.TryParse(text, out var number) => number,
            _ => null,
        };
    }

    private static InvalidOperationException Missing(string key)
        => new($"The delivery envelope is missing '{key}'.");
}
