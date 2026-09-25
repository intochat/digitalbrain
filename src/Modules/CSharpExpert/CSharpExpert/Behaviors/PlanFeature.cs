using System.Text;
using DigitalBrain.Contracts;
using DigitalBrain.Core;

namespace DigitalBrain.CSharpExpert;

public sealed class PlanFeature(IDigitalBrain brain, string runId) : IBehavior, IBehaviorSignals
{
    public IReadOnlyList<Type> Signals => [typeof(ContextReady), typeof(PlanClarified)];

    public Task RunAsync(CancellationToken cancellation = default)
    {
        var run = brain.Get<ICodingRun>(runId);
        return Task.WhenAll(
            ListenAsync<ContextReady>(run, cancellation),
            ListenAsync<PlanClarified>(run, cancellation));
    }

    private async Task ListenAsync<TSignal>(ICodingRun run, CancellationToken cancellation) where TSignal : Signal
    {
        await foreach (var _ in brain.On<TSignal>(run, cancellation).ConfigureAwait(false))
        {
            try
            {
                await DraftAsync(run, cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                await run.Fail($"Planning failed: {error.Message}").ConfigureAwait(false);
            }
        }
    }

    private async Task DraftAsync(ICodingRun run, CancellationToken cancellation)
    {
        var snapshot = await run.Read().ConfigureAwait(false);
        var request = snapshot.Request ?? throw new InvalidOperationException("Planning started before the feature request.");
        var model = snapshot.Model ?? throw new InvalidOperationException("Planning started before the project context.");
        var profile = await brain.Get<ICodingProfile>(CodingWorkspace.Id(request.SolutionPath)).Read().ConfigureAwait(false);
        var agent = brain.Get<ICodingAgent>(profile.PlannerAgentId);
        var reply = await agent.Ask(PlannerPrompt(request, model, snapshot.Clarifications), cancellation).ConfigureAwait(false);
        await run.RecordPlan(CodingPlanReader.Read(reply)).ConfigureAwait(false);
    }

    private static string PlannerPrompt(FeatureRequest request, ProjectModel model, IReadOnlyList<string> clarifications)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Plan a change to a C# solution. Reply with one JSON object and nothing else.");
        builder.AppendLine($"Request: {request.Description}");
        builder.AppendLine($"Solution: {request.SolutionPath}");
        builder.AppendLine($"Projects: {string.Join(", ", model.Projects.Select(project => project.Name))}");
        builder.AppendLine($"Target frameworks: {string.Join(", ", model.TargetFrameworks)}");
        builder.AppendLine("Map:");
        builder.AppendLine(model.MapText);
        if (clarifications.Count > 0)
        {
            builder.AppendLine("Clarifications from the user (honor every one):");
            foreach (var clarification in clarifications)
            {
                builder.AppendLine($"- {clarification}");
            }
        }

        builder.AppendLine("Use only file paths exactly as listed in the map (relative to the solution folder); a new file goes next to its siblings.");
        builder.AppendLine("Keep steps small and ordered; each step must build and keep tests green on its own.");
        builder.AppendLine("JSON shape: {\"summary\":\"...\",\"steps\":[{\"number\":1,\"title\":\"...\",\"files\":[\"path\"],\"detail\":\"...\"}],\"openQuestions\":[\"...\"]}");
        return builder.ToString();
    }
}
