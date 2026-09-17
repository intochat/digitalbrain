using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;


namespace DigitalBrain.Abstractions.Behavior;

[Alias("behavior-run")]
public interface IBehaviorRun : INeuron
{
    [ReadOnly, Alias("read")]
    [NeuronTool(IsReadOnly = true)]
    Task<BehaviorRunSnapshot?> Read();
}
