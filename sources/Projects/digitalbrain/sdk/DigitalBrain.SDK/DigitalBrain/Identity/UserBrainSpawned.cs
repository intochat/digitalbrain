using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.SDK.DigitalBrain.Identity;

[Signal(Fqn)]
[GenerateSerializer]
public sealed record UserBrainSpawned(
    [property: Id(0)] string UserId,
    [property: Id(1)] string SessionToken) : Synapse
{
    public const string Fqn = "DigitalBrain.SDK.Identity.Contracts.UserBrainSpawned";
}
