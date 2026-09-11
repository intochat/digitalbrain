using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.UI;

[Alias("ui.activities")]
public interface IActivities : INeuron
{
    /// <summary>Reads the most recently updated activity views.</summary>
    [ReadOnly, Alias("read")]
    Task<ActivitiesSnapshot> Read(ReadActivities query);
}
