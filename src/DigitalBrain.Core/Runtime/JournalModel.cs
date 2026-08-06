using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalBrain;

internal enum SpeechRole
{
    Fanout,
}

internal sealed record JournalEntry(
    long Position,
    string Entry,
    string Kind,
    DateTimeOffset At,
    SynapseRefEntry? Cause,
    SynapseRefEntry? From,
    DeliveryTarget[]? To,
    JsonElement Body,
    SpeechRole? Role)
{
    internal const string Heard = "heard";
    internal const string Said = "said";

    internal DeliveryEnvelope ToEnvelope(NeuronId owner)
        => From is { } source
            ? new DeliveryEnvelope(source.ToSynapseRef().Source, source.Position, At, Cause?.ToSynapseRef())
            : new DeliveryEnvelope(owner, Position, At, Cause?.ToSynapseRef());
}

internal sealed record SynapseRefEntry(string Kind, string Name, long Position)
{
    internal static SynapseRefEntry From(SynapseRef reference)
        => new(reference.Source.Kind, reference.Source.Name, reference.Sequence);

    internal SynapseRef ToSynapseRef() => new(new NeuronId(Kind, Name), Position);
}

internal sealed record DeliveryTarget(string Kind, string Name)
{
    internal static DeliveryTarget From(NeuronId id) => new(id.Kind, id.Name);

    internal NeuronId ToNeuronId() => new(Kind, Name);
}

internal sealed record DeliveryProgress(DeliveryTarget[] Pending, int Attempts);

internal sealed record WatermarkEntry(long Position);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JournalEntry))]
[JsonSerializable(typeof(SynapseRefEntry))]
[JsonSerializable(typeof(DeliveryTarget))]
[JsonSerializable(typeof(DeliveryTarget[]))]
[JsonSerializable(typeof(DeliveryProgress))]
[JsonSerializable(typeof(WatermarkEntry))]
[JsonSerializable(typeof(SpeechRole))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(TimeSpan))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(int))]
internal sealed partial class JournalJsonContext : JsonSerializerContext;
