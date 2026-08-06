using System.Text.Json;

namespace DigitalBrain;

internal interface ISynapseSerialization
{
    JsonElement Serialize(object value);

    object? Deserialize(JsonElement element, Type type);

    Synapse? DeserializeForDispatch(string synapseKind, JsonElement serialization);
}
