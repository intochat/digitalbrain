using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;


namespace DigitalBrain.Abstractions.Behavior;

[Alias("behavior")]
public interface IBehavior : INeuron
{
    [ReadOnly, Alias("read")]
    [NeuronTool(IsReadOnly = true)]
    Task<BehaviorSnapshot> Read();
}
