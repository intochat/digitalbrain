using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.SDK.DigitalBrain.Identity;

[Signal(Fqn)]
[GenerateSerializer]
public sealed record CreateBrainResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] string BrainId,
    [property: Id(2)] string SessionToken,
    [property: Id(3)] string ErrorMessage) : Synapse
{
    public const string Fqn = "DigitalBrain.SDK.Identity.Contracts.CreateBrainResult";
}
