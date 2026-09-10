using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.UI;

/// <summary>Records an image description and its stored blob reference.</summary>
[GenerateSerializer]
[Alias("ui.describe-image")]
public sealed record DescribeImage(
    CommandId Id,
    [property: Id(0)] string Prompt,
    [property: Id(1)] string Model,
    [property: Id(2)] string MediaType,
    [property: Id(3)] string BlobName) : Command(Id);
