using Microsoft.CodeAnalysis;

namespace DigitalBrain.Coding;

// Failures are the workspace diagnostics a loader saw while opening (a project that did not evaluate,
// a reference that did not resolve); the solution still opened, so they travel next to it.
public sealed record LoadedSolution(Workspace Workspace, IReadOnlyList<string> Failures);

public interface ISolutionLoader
{
    Task<LoadedSolution> OpenAsync(string solutionPath, IProgress<string> progress, CancellationToken cancellationToken);
}
