using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.SDK.Microsoft.Aspire;

[Signal(Identity)]
public sealed record AppStartedSignal(string Profile) : Synapse
{
    public const string Identity = "DigitalBrain.SDK.Aspire.AppStarted";
}
