using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer;

/// <summary>
/// A strongly-typed C# interface representing a file in the workspace, keyed by its absolute path.
/// Translates plain-English BDD scenarios into secure, compile-time checked async operations.
/// </summary>
public interface IFileNeuron : INeuronWithStringKey, IResourceNeuronTarget
{
    /// <summary>
    /// Gets the absolute path of the file.
    /// </summary>
    Task<string> GetFilePathAsync();

    /// <summary>
    /// Gets the raw content of the file.
    /// </summary>
    Task<string> GetContentAsync();

    /// <summary>
    /// Safely applies edits to the file, returning true if successful.
    /// </summary>
    Task<bool> ApplyEditAsync(string newContent, string? commitMessage = null);
}
