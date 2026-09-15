namespace DigitalBrain.Twitter;

[GenerateSerializer]
[Alias("twitter.posted")]
public sealed record Posted(
    [property: Id(0)] string EventId,
    [property: Id(1)] string Author,
    [property: Id(2)] string Text);
