using DigitalBrain.Core.Synapses;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Core.Neurons;

public readonly record struct NeuronId(string Value);

public abstract class Neuron([FromKeyedServices("synaspes")] IDurableList<Signal> connections)
    : DurableGrain, INeuron, IGrainWithStringKey
{

}