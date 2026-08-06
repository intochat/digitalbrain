using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace DigitalBrain;

internal interface ICatalog
{
    IReadOnlyCollection<Type> FactTypes { get; }

    bool TryGetFactType(string kind, [NotNullWhen(true)] out Type? factType);

    string KindOfFact(Type factType);

    IReadOnlyCollection<string> ListenerKindsOf(Type factType);

    bool HasNeuronKind(string kind);
}

internal interface ISynapseCodec
{
    JsonElement Encode(object value);

    object? Decode(JsonElement element, Type type);

    Synapse? DecodeFact(string kind, JsonElement body);
}

internal interface IEnvelopeCarrier
{
    void Write(DeliveryEnvelope envelope);

    DeliveryEnvelope? Consume();
}
