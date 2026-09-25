using DigitalBrain.Contracts;
using DigitalBrain.Core;

namespace DigitalBrain.CSharpExpert;

public sealed class ImplementStep(IDigitalBrain brain, string runId) : IBehavior, IBehaviorSignals
{
    public IReadOnlyList<Type> Signals => [typeof(WorkspacePrepared), typeof(StepDone)];

    public Task RunAsync(CancellationToken cancellation = default)
    {
        var run = brain.Get<ICodingRun>(runId);
        return Task.WhenAll(
            ListenAsync<WorkspacePrepared>(run, cancellation),
            ListenAsync<StepDone>(run, cancellation));
    }

    internal static PlanStep StepAt(CodingRunSnapshot snapshot)
    {
        var plan = snapshot.Plan ?? throw new InvalidOperationException("Implementing started before the plan was recorded.");
        var index = snapshot.CurrentStep - 1;
        if (index < 0 || index >= plan.Steps.Count)
        {
            throw new InvalidOperationException($"Step {snapshot.CurrentStep} is outside the plan ({plan.Steps.Count} steps).");
        }

        return plan.Steps[index];
    }

    private async Task ListenAsync<TSignal>(ICodingRun run, CancellationToken cancellation) where TSignal : Signal
    {
        await foreach (var _ in brain.On<TSignal>(run, cancellation).ConfigureAwait(false))
        {
            try
            {
                var snapshot = await run.Read().ConfigureAwait(false);
                await StepEditing.ApplyAsync(brain, run, snapshot, StepAt(snapshot), failure: null, cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                await run.Fail($"Implementing step failed: {error.Message}").ConfigureAwait(false);
            }
        }
    }
}
