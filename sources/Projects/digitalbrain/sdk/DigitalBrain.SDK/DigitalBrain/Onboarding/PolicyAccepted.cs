using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.SDK.DigitalBrain.Onboarding;

[Signal(Fqn)]
[GenerateSerializer]
public sealed record PolicyAccepted(
    [property: Id(0)] string UserId,
    [property: Id(1)] string Version) : Synapse
{
    public const string Fqn = "DigitalBrain.Domains.Onboarding.Contracts.PolicyAccepted";
}
