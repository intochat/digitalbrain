using System.Text;
using DigitalBrain.Contracts;
using DigitalBrain.Core;

namespace DigitalBrain.CSharpExpert;

public sealed class PlanFeature(IDigitalBrain brain, string runId) : IBehavior
{
    public async Task RunAsync(CancellationToken cancellation = default)
    {
        var run = brain.Get<ICodingRun>(runId);
        await foreach (var context in brain.On<ContextReady>(run, cancellation).ConfigureAwait(false))
        {
            var snapshot = await run.Read().ConfigureAwait(false);
            var request = snapshot.Request ?? throw new InvalidOperationException("Context arrived before the feature request.");
            var profile = await brain.Get<ICodingProfile>(CodingWorkspace.Id(request.SolutionPath)).Read().ConfigureAwait(false);
            var agent = brain.Get<ICodingAgent>(profile.PlannerAgentId);
            var reply = await agent.Ask(PlannerPrompt(request, context.Model), cancellation).ConfigureAwait(false);
            CodingPlan plan;
            try
            {
                plan = CodingPlanReader.Read(reply);
            }
            catch (FormatException error)
            {
                await run.Fail(error.Message).ConfigureAwait(false);
                continue;
            }

            await run.RecordPlan(plan).ConfigureAwait(false);
        }
    }

    private static string PlannerPrompt(FeatureRequest request, ProjectModel model)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Plan a change to a C# solution. Reply with one JSON object and nothing else.");
        builder.AppendLine($"Request: {request.Description}");
        builder.AppendLine($"Solution: {request.SolutionPath}");
        builder.AppendLine($"Projects: {string.Join(", ", model.Projects.Select(project => project.Name))}");
        builder.AppendLine($"Target frameworks: {string.Join(", ", model.TargetFrameworks)}");
        builder.AppendLine("Map:");
        builder.AppendLine(model.MapText);
        builder.AppendLine("JSON shape: {\"summary\":\"...\",\"steps\":[{\"number\":1,\"title\":\"...\",\"files\":[\"path\"],\"detail\":\"...\"}],\"openQuestions\":[\"...\"]}");
        return builder.ToString();
    }
}
