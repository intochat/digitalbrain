using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Neurons;

// Lifecycle coordination is not part of a neuron's callable capability surface.
[Alias("db.v2.neuron-ownership")]
public interface INeuronOwnership : IGrainWithStringKey
{
    [Alias(nameof(Claim))]
    Task Claim(string owner);

    [Alias(nameof(Release))]
    Task Release(string owner);

    [Alias(nameof(ConnectFor))]
    Task ConnectFor(string owner, NeuronId target, string signalType);

    [Alias(nameof(DisconnectFor))]
    Task DisconnectFor(string owner, NeuronId target, string signalType);
}
