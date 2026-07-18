using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Google;

[GenerateSerializer]
public sealed record VideoFound([property: Id(1)] string UserAccountId,
    [property: Id(2)] YouTubeVideo Video
) : Synapse;
