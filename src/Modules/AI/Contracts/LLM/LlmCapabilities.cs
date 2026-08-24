namespace DigitalBrain.AI;

/// <summary>
/// What a chat model is known to support.
/// </summary>
/// <remarks>
/// Flags rather than a record of booleans: an undeclared flag means "not claimed
/// here", not "proven absent". Adding a capability to a model is then additive
/// and truthful, where a boolean would force every model to assert something
/// about a feature nobody has verified for it.
/// </remarks>
[Flags]
public enum LlmCapabilities
{
    None = 0,

    /// <summary>Can emit tool calls. Models without this are never shown tools.</summary>
    Tools = 1,

    /// <summary>Accepts image content in the prompt.</summary>
    Vision = 1 << 1,

    /// <summary>Can be constrained to a response schema.</summary>
    StructuredOutput = 1 << 2,
}
