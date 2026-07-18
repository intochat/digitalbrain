using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer;

/// <summary>
/// A strongly-typed C# interface representing Dotnet command executions.
/// </summary>
public interface IDotnet : INeuron
{
    /// <summary>
    /// Ask the Dotnet neuron to run a dotnet command.
    /// </summary>
    Task<string> AskAsync(string prompt);
}
