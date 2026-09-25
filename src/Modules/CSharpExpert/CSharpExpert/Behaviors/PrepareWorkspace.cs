using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Microsoft.Roslyn;

namespace DigitalBrain.CSharpExpert;

public sealed class PrepareWorkspace(IDigitalBrain brain, string runId, WorkspacePreparer preparer) : IBehavior, IBehaviorSignals
{
    public IReadOnlyList<Type> Signals => [typeof(PlanApproved)];

    public async Task RunAsync(CancellationToken cancellation = default)
    {
        var run = brain.Get<ICodingRun>(runId);
        await foreach (var _ in brain.On<PlanApproved>(run, cancellation).ConfigureAwait(false))
        {
            try
            {
                var snapshot = await run.Read().ConfigureAwait(false);
                var request = snapshot.Request ?? throw new InvalidOperationException("Workspace preparation started before the feature request.");
                var location = await preparer.PrepareAsync(request.SolutionPath, runId, cancellation).ConfigureAwait(false);
                var roslyn = brain.Get<IRoslyn>(CodingWorkspace.Id(location.SolutionPath));
                await RoslynWorkspace.OpenAsync(roslyn, location.SolutionPath, cancellation).ConfigureAwait(false);
                await run.PrepareWorkspace(location.Root, location.SolutionPath).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                await run.Fail($"Preparing the workspace failed: {error.Message}").ConfigureAwait(false);
            }
        }
    }
}
