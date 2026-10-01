using System.Text.Json.Serialization;

namespace DigitalBrain.AI;

// Flags rather than a record of booleans: an undeclared flag means "not claimed
// here", not "proven absent". Adding a capability to a model is then additive
// and truthful, where a boolean would force every model to assert something
// about a feature nobody has verified for it.
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<LlmCapabilities>))]
public enum LlmCapabilities
{
    None = 0,

    // Models without this are never shown tools.
    Tools = 1,

    Vision = 1 << 1,

    StructuredOutput = 1 << 2,
}
