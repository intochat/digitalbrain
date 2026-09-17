using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter;

[Alias("ui.activities")]
public interface IActivities : INeuron
{
    /// <summary>Reads the most recently updated activity views.</summary>
    [ReadOnly, Alias("read")]
    [NeuronTool(IsReadOnly = true)]
    Task<ActivitiesSnapshot> Read(ReadActivities query);
}
