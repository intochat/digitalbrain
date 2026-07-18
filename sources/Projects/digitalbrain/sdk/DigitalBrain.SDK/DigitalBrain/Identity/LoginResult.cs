using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Identity;

[GenerateSerializer]
public sealed record LoginResult([property: Id(1)] bool Success,
    [property: Id(2)] string UserId,
    [property: Id(3)] string? ErrorMessage,
    [property: Id(4)] string? SessionToken
) : Synapse;
