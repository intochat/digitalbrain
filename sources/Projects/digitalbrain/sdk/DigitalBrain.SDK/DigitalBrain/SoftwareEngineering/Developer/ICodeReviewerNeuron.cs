using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer;

/// <summary>
/// A strongly-typed C# interface representing multi-LLM consensus code review.
/// </summary>
public interface ICodeReviewerNeuron : INeuron
{
    /// <summary>
    /// Run a strict bug finder, security audit, and compile a consensus code review diff.
    /// </summary>
    Task<ReviewCodeResponse> ReviewDiffAsync(string diff, string? targetFile = null);
}
