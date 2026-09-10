namespace DigitalBrain.Chat;

/// <summary>Identifies context by path and schema with an optional inline payload or blob reference.</summary>
[GenerateSerializer]
[Alias("chat.context-ref")]
public sealed record ContextRef(
    [property: Id(0)] string Path,
    [property: Id(1)] string SchemaHash,
    [property: Id(2)] string? PayloadJson = null,
    [property: Id(3)] string? BlobRef = null);
