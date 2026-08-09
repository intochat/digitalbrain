using DigitalBrain.Poc.Abstractions;
using Orleans;

namespace DigitalBrain.Poc.Social.Contracts;

[GenerateSerializer]
[Alias("db.poc.social.post-observed.v1")]
public sealed record SocialPostObserved(
    [property: Id(0)] string PostId,
    [property: Id(1)] string Author,
    [property: Id(2)] System.DateTimeOffset OccurredAt) : Synapse;
