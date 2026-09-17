using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;


namespace DigitalBrain.Abstractions.Behavior;

[Alias("behavior-catalog")]
public interface IBehaviorCatalog : INeuron
{
    [ReadOnly, Alias("list")]
    [NeuronTool(IsReadOnly = true)]
    Task<IReadOnlyList<string>> List();
}
