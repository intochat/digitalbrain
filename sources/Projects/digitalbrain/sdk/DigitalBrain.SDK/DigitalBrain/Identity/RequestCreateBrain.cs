using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Identity;

[GenerateSerializer]
public sealed record RequestCreateBrain([property: Id(1)] string UserId,
    [property: Id(2)] string NewBrainId,
    [property: Id(3)] string? SourceBrainId,
    [property: Id(4)] string SyncTarget = "local"
) : Synapse;
