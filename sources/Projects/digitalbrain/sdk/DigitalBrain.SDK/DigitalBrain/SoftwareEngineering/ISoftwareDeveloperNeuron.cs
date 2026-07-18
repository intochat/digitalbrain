using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering;

/// <summary>
/// A strongly-typed C# interface representing the autonomous, self-healing Software Developer Neuron (Antigravity 2.0).
/// </summary>
public interface ISoftwareDeveloperNeuron : INeuron
{
    Task<EngineeringTaskResponse> ExecuteTaskAsync(EngineeringTaskRequest request);
}
