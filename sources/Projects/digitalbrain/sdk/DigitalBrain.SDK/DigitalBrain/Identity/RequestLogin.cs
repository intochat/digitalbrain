using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Identity;

[GenerateSerializer]
public sealed record RequestLogin([property: Id(1)] string Username,
    [property: Id(2)] string Password
) : Synapse;
