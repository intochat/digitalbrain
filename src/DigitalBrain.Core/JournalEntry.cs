using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalBrain;

// The closed durable schema family — the only element types Orleans.Journaling ever
// persists for a neuron. Fact and state bodies travel as pre-encoded JsonElement, so no
// module CLR type ever enters JournalJsonContext: journals outlive the code that wrote
// them by construction. One closed vocabulary, read as a set.
// Seq: own-journal position; for "said" this IS SynapseRef.Sequence.
// Entry: "heard" | "said".
// Kind: boot-catalog factKind, minted by NeuronId.KindOf.
// At: said = turn commit time (the retry horizon runs from it); heard = the emitter's
//     envelope timestamp.
// Cause: turn causation; null = edge-born only (ticks carry the schedule entry's ref).
// Answers: said = the answer emission; heard = copied from the reply's envelope — edge
//     polls and continuation dispatch match on it.
// From: heard only; the emission's identity = the dedup key.
// To: said only; receiver snapshot + provenance; [] = zero-receiver.
// Body: Core body codec output; opaque to Orleans.Journaling.
internal sealed record JournalEntry(
    long Seq,
    string Entry,
    string Kind,
    DateTimeOffset At,
    SynapseRefEntry? Cause,
    SynapseRefEntry? Answers,
    SynapseRefEntry? From,
    NeuronIdEntry[]? To,
    JsonElement Body)
{
    internal const string Heard = "heard";
    internal const string Said = "said";

    internal SynapseMetadata ToMetadata(NeuronId journalOwner) => From is { } emission
        ? new SynapseMetadata(new NeuronId(emission.Kind, emission.Name), emission.Seq, At,
            Cause?.ToSynapseRef(), Answers?.ToSynapseRef())
        : new SynapseMetadata(journalOwner, Seq, At, Cause?.ToSynapseRef(), Answers?.ToSynapseRef());
}

internal sealed record SynapseRefEntry(string Kind, string Name, long Seq)
{
    internal static SynapseRefEntry From(SynapseRef reference)
        => new(reference.Source.Kind, reference.Source.Name, reference.Sequence);

    internal SynapseRef ToSynapseRef() => new(new NeuronId(Kind, Name), Seq);
}

internal sealed record NeuronIdEntry(string Kind, string Name, string? Via)
{
    // The complete Via vocabulary of a said entry's receiver snapshot.
    internal const string Declared = "declared";
    internal const string Connected = "connected";
    internal const string Ask = "ask";

    internal static NeuronIdEntry From(NeuronId id, string? via) => new(id.Kind, id.Name, via);

    internal NeuronId ToNeuronId() => new(Kind, Name);
}

// The scheduled fact stays JsonElement — no module CLR type in durable data. Cause is the
// own-journal position of the entry that recorded this schedule (the verb's said entry or
// the remote Schedule reception); every tick's heard entry carries it as its Cause.
internal sealed record ScheduleEntry(
    string Kind,
    JsonElement Fact,
    TimeSpan Period,
    DateTimeOffset NextDue,
    int ConsecutiveFailures,
    long Cause);

internal sealed record DeliveryProgress(NeuronIdEntry[] Pending, int Attempts);

internal sealed record WatermarkEntry(long Seq, DateTimeOffset Touched);

// The bare-resolver invariant: Orleans.Journaling wraps this context without options-level
// converters, so the registrations below are the complete set of types that ever touch the
// journaling surface — Core's closed entries, the primitives they carry, and the package's
// own bookkeeping (uint, DateTime). No module CLR type ever enters this context; module
// payloads arrive here only as JsonElement already encoded by the body codec.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JournalEntry))]
[JsonSerializable(typeof(SynapseRefEntry))]
[JsonSerializable(typeof(NeuronIdEntry))]
[JsonSerializable(typeof(NeuronIdEntry[]))]
[JsonSerializable(typeof(ScheduleEntry))]
[JsonSerializable(typeof(DeliveryProgress))]
[JsonSerializable(typeof(WatermarkEntry))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(TimeSpan))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(int))]
internal sealed partial class JournalJsonContext : JsonSerializerContext;
