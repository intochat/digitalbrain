namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.context-digest")]
public sealed record ContextDigest(
    [property: Id(0)] string Path,
    [property: Id(1)] string Digest);
