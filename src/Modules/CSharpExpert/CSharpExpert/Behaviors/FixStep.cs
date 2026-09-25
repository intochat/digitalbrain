using DigitalBrain.Contracts;
using DigitalBrain.Core;

namespace DigitalBrain.CSharpExpert;

public sealed class FixStep(IDigitalBrain brain, string runId) : IBehavior, IBehaviorSignals
{
    public IReadOnlyList<Type> Signals => [typeof(BuildFailed), typeof(TestsFailed)];

    public Task RunAsync(CancellationToken cancellation = default)
    {
        var run = brain.Get<ICodingRun>(runId);
        return Task.WhenAll(
            ListenAsync<BuildFailed>(run, BuildFailureText, cancellation),
            ListenAsync<TestsFailed>(run, TestFailureText, cancellation));
    }

    private static string BuildFailureText(BuildFailed signal)
        => "Build failed:" + Environment.NewLine
            + string.Join(Environment.NewLine, signal.Diagnostics.Select(diagnostic => $"{diagnostic.Id} ({diagnostic.File}): {string.Join("; ", diagnostic.Messages)}"));

    private static string TestFailureText(TestsFailed signal)
        => "Tests failed:" + Environment.NewLine
            + string.Join(Environment.NewLine, signal.Failures.Select(failure => $"{failure.Name}: {failure.Message}"));

    private async Task ListenAsync<TSignal>(ICodingRun run, Func<TSignal, string> describe, CancellationToken cancellation) where TSignal : Signal
    {
        await foreach (var signal in brain.On<TSignal>(run, cancellation).ConfigureAwait(false))
        {
            try
            {
                var snapshot = await run.Read().ConfigureAwait(false);
                var request = snapshot.Request ?? throw new InvalidOperationException("Fixing started before the feature request.");
                var profile = await brain.Get<ICodingProfile>(CodingWorkspace.Id(request.SolutionPath)).Read().ConfigureAwait(false);
                var failure = describe(signal);
                if (snapshot.FixAttempts >= profile.MaxFixAttempts)
                {
                    await run.RecordNeedsHuman($"Gave up on step {snapshot.CurrentStep} after {snapshot.FixAttempts} fix attempts. {failure}").ConfigureAwait(false);
                    continue;
                }

                await StepEditing.ApplyAsync(brain, run, snapshot, ImplementStep.StepAt(snapshot), failure, cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                await run.Fail($"Fixing step failed: {error.Message}").ConfigureAwait(false);
            }
        }
    }
}
