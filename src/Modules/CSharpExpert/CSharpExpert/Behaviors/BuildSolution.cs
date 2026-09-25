using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Microsoft.DotNet;

namespace DigitalBrain.CSharpExpert;

public sealed class BuildSolution(IDigitalBrain brain, string runId) : IBehavior, IBehaviorSignals
{
    public IReadOnlyList<Type> Signals => [typeof(StepDrafted)];

    public async Task RunAsync(CancellationToken cancellation = default)
    {
        var run = brain.Get<ICodingRun>(runId);
        await foreach (var _ in brain.On<StepDrafted>(run, cancellation).ConfigureAwait(false))
        {
            try
            {
                var snapshot = await run.Read().ConfigureAwait(false);
                var solutionPath = snapshot.WorkspaceSolutionPath ?? throw new InvalidOperationException("The workspace was not prepared.");
                var outcome = await brain.Get<IDotnet>("dotnet").Build(new BuildRequest(solutionPath, null), cancellation).ConfigureAwait(false);
                await run.RecordBuild(outcome).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                await run.Fail($"Building the solution failed: {error.Message}").ConfigureAwait(false);
            }
        }
    }
}
