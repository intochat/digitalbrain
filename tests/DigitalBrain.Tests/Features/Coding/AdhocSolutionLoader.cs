using DigitalBrain.Coding;
using Microsoft.CodeAnalysis;

namespace DigitalBrain.Tests.Coding;

internal sealed class AdhocSolutionLoader(Func<Workspace> open) : ISolutionLoader
{
    public int Opens { get; private set; }

    public Task<LoadedSolution> OpenAsync(string solutionPath, IProgress<string> progress, CancellationToken cancellationToken)
    {
        Opens++;
        progress.Report($"opened {solutionPath}");
        return Task.FromResult(new LoadedSolution(open(), []));
    }
}
