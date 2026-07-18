namespace Ino.Core;

/// <summary>
/// Typed error carried by a failed NeuronResult. Error codes are searchable on the
/// timeline and are the primary signal for self-improvement pattern extraction.
/// </summary>
[GenerateSerializer]
public sealed record SynapseError(
    [property: Id(0)] SynapseErrorCode Code,
    [property: Id(1)] string Message,
    [property: Id(2)] IReadOnlyDictionary<string, string>? Details = null);
