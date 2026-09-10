namespace DigitalBrain.UI;

// A slot's position in the context list is its ordinal.
[GenerateSerializer, Alias("db.ui.context-slot")]
public sealed record ContextSlot(
    [property: Id(0)] string Path,
    [property: Id(1)] string SchemaHash,
    [property: Id(2)] string? PayloadJson,
    [property: Id(3)] string? BlobRef,
    [property: Id(4)] string Digest);
