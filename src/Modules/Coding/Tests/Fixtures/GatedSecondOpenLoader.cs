using DigitalBrain.Coding;
using Microsoft.CodeAnalysis;

namespace DigitalBrain.Tests.Coding;

// The first open completes at once; every later one waits for the gate, so a fact can observe "Opening"
// for as long as it needs to and release it when it is done.
internal sealed class GatedSecondOpenLoader(Func<Workspace> open, TaskCompletionSource gate) : ISolutionLoader
{
    private int _opens;

    public async Task<LoadedSolution> OpenAsync(string solutionPath, IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _opens) > 1)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        return new LoadedSolution(open(), []);
    }
}
