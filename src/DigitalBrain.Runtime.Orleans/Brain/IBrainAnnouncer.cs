using DigitalBrain.Access;
using DigitalBrain.Brains;
using DigitalBrain.Contracts;

namespace DigitalBrain.Runtime.Orleans.Brain;

/// <summary>
/// The kernel grains' way into the brain neuron: one-way and unstamped, since the
/// brain neuron's own first write is registered through the same directory that announces it.
/// </summary>
[NeuronAlias("kernel.announcer"), DefaultNeuronType(IBrain.Type), Internal]
public interface IBrainAnnouncer
{
    [OneWayCall] Task InstanceCreated(string type, string key);
    [OneWayCall] Task InstanceRetired(string type, string key);
    [OneWayCall] Task TypeAdded(string name);
}
