using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Twitter;

[GenerateSerializer]
[Alias("twitter.post")]
public sealed record Post(
    CommandId Id,
    [property: Id(0)] string ProviderPostId,
    [property: Id(1)] string Author,
    [property: Id(2)] string Text) : Command(Id);
