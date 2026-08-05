using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalBrain;

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

    internal SynapseMetadata ToMetadata(NeuronId journalOwner)
        => ToEnvelope(journalOwner).Identity;

    internal DeliveryEnvelope ToEnvelope(NeuronId journalOwner)
    {
        if (From is { } emission)
        {
            return new DeliveryEnvelope(
                new NeuronId(emission.Kind, emission.Name),
                emission.Seq,
                At,
                Cause?.ToSynapseRef(),
                Answers?.ToSynapseRef());
        }

        return new DeliveryEnvelope(
            journalOwner,
            Seq,
            At,
            Cause?.ToSynapseRef(),
            Answers?.ToSynapseRef());
    }
}

internal sealed record SynapseRefEntry(string Kind, string Name, long Seq)
{
    internal static SynapseRefEntry From(SynapseRef reference)
        => new(reference.Source.Kind, reference.Source.Name, reference.Sequence);

    internal SynapseRef ToSynapseRef() => new(new NeuronId(Kind, Name), Seq);
}

internal sealed record NeuronIdEntry(string Kind, string Name, string? Via)
{
    internal const string Declared = "declared";
    internal const string Connected = "connected";
    internal const string Ask = "ask";

    internal static NeuronIdEntry From(NeuronId id, string? via) => new(id.Kind, id.Name, via);

    internal NeuronId ToNeuronId() => new(Kind, Name);
}

internal sealed record ScheduleEntry(
    string Kind,
    JsonElement Fact,
    TimeSpan Period,
    DateTimeOffset NextDue,
    int ConsecutiveFailures,
    long Cause);

internal sealed record DeliveryProgress(NeuronIdEntry[] Pending, int Attempts);

internal sealed record WatermarkEntry(long Seq, DateTimeOffset Touched);

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
