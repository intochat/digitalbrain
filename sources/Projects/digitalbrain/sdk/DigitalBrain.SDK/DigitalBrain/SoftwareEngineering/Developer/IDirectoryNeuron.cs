using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer;

/// <summary>
/// A strongly-typed C# interface representing a directory in the workspace, keyed by its absolute path.
/// </summary>
public interface IDirectoryNeuron : INeuronWithStringKey, IResourceNeuronTarget
{
    /// <summary>
    /// Gets the absolute path of the directory.
    /// </summary>
    Task<string> GetDirectoryPathAsync();

    /// <summary>
    /// Returns the absolute paths of all child files.
    /// </summary>
    Task<IReadOnlyList<string>> GetFilesAsync();

    /// <summary>
    /// Returns the absolute paths of all child directories.
    /// </summary>
    Task<IReadOnlyList<string>> GetDirectoriesAsync();
}
