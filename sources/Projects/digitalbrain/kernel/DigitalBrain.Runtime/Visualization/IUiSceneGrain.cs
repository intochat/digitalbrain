using System.Threading.Tasks;
using Orleans;

namespace DigitalBrain.Runtime.Visualization;

[Alias("DigitalBrain.Runtime.Visualization.IUiSceneGrain")]
public interface IUiSceneGrain : IGrainWithStringKey
{
    [Alias("GetLayoutAsync")]
    Task<(string RfwTemplate, string DataJson)> GetLayoutAsync(string layoutName, string? neuronId = null);
}
