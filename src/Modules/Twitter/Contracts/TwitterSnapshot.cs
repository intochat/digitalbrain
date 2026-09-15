namespace DigitalBrain.Twitter;

[GenerateSerializer]
[Alias("twitter.snapshot")]
public sealed record TwitterSnapshot(
    [property: Id(0)] long PublishedCount,
    [property: Id(1)] int RetainedPostIds,
    [property: Id(2)] Posted? LastPost);
