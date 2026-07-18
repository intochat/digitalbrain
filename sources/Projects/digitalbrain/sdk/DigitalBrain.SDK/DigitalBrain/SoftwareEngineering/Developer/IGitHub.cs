using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer;

/// <summary>
/// A strongly-typed C# interface representing Git and GitHub automation commands.
/// </summary>
public interface IGitHub : INeuronWithStringKey
{
    /// <summary>
    /// Gets the current Git status of the local workspace.
    /// </summary>
    Task<GitStatusResponse> GetStatusAsync();

    /// <summary>
    /// Staging and committing changes to Git.
    /// </summary>
    Task<bool> CommitAsync(string message, IReadOnlyList<string>? files = null, bool autoStage = true);

    /// <summary>
    /// Creating and submitting a pull request via the GitHub CLI.
    /// </summary>
    Task<bool> SubmitPullRequestAsync(string title, string body, string sourceBranch, string targetBranch = "master", bool draft = false);
}
