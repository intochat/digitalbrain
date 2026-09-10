namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.image-state")]
public sealed record ImageState(
    [property: Id(0)] string Prompt,
    [property: Id(1)] string Model,
    [property: Id(2)] string MediaType,
    [property: Id(3)] string BlobName);
