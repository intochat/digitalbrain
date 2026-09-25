using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Microsoft.DotNet;
using DigitalBrain.Microsoft.Roslyn;

namespace DigitalBrain.CSharpExpert;

public sealed class RunTests(IDigitalBrain brain, string runId) : IBehavior, IBehaviorSignals
{
    private const string TestProjectSuffix = ".Tests";

    public IReadOnlyList<Type> Signals => [typeof(BuildPassed)];

    public async Task RunAsync(CancellationToken cancellation = default)
    {
        var run = brain.Get<ICodingRun>(runId);
        await foreach (var _ in brain.On<BuildPassed>(run, cancellation).ConfigureAwait(false))
        {
            try
            {
                var snapshot = await run.Read().ConfigureAwait(false);
                var request = snapshot.Request ?? throw new InvalidOperationException("Testing started before the feature request.");
                var map = await brain.Get<IRoslyn>(CodingWorkspace.Id(RunWorkspace.SolutionPath(snapshot))).Map(new MapQuery(), cancellation).ConfigureAwait(false);
                var projects = map.Projects.Where(project => project.Name.EndsWith(TestProjectSuffix, StringComparison.Ordinal)).ToArray();
                var dotnet = brain.Get<IDotnet>("dotnet");
                var failures = new List<TestFailure>();
                var invocations = new List<string>();
                var passed = 0;
                var failed = 0;
                var skipped = 0;
                var duration = 0d;
                var succeeded = true;
                foreach (var project in projects)
                {
                    var outcome = await dotnet.Test(new TestRequest(project.Path, null, null), cancellation).ConfigureAwait(false);
                    passed += outcome.Passed;
                    failed += outcome.Failed;
                    skipped += outcome.Skipped;
                    duration += outcome.DurationSeconds;
                    failures.AddRange(outcome.Failures);
                    invocations.Add(outcome.Invocation);
                    succeeded &= outcome.Succeeded;
                }

                var aggregated = new TestOutcome(
                    succeeded, passed + failed + skipped, passed, failed, skipped, failures, duration,
                    string.Join(" ; ", invocations), succeeded ? null : "one or more test projects failed");
                await run.RecordTests(aggregated).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                await run.Fail($"Running tests failed: {error.Message}").ConfigureAwait(false);
            }
        }
    }
}
