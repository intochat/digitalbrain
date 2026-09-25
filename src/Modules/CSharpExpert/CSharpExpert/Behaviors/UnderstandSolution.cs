using System.Text;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Microsoft.Roslyn;

namespace DigitalBrain.CSharpExpert;

public sealed class UnderstandSolution(IDigitalBrain brain, string runId) : IBehavior
{
    public async Task RunAsync(CancellationToken cancellation = default)
    {
        var run = brain.Get<ICodingRun>(runId);
        await foreach (var signal in brain.On<FeatureRequested>(run, cancellation).ConfigureAwait(false))
        {
            try
            {
                var model = await ReadModelAsync(signal.Request, cancellation).ConfigureAwait(false);
                await run.RecordContext(model).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                await run.Fail($"Reading '{signal.Request.SolutionPath}' failed: {error.Message}").ConfigureAwait(false);
            }
        }
    }

    private async Task<ProjectModel> ReadModelAsync(FeatureRequest request, CancellationToken cancellation)
    {
        var roslyn = brain.Get<IRoslyn>(CodingWorkspace.Id(request.SolutionPath));
        await roslyn.Open(new OpenWorkspace(request.SolutionPath)).ConfigureAwait(false);
        var map = await roslyn.Map(new MapQuery(), cancellation).ConfigureAwait(false);
        var diagnostics = await roslyn.Diagnostics(new DiagnosticsQuery(), cancellation).ConfigureAwait(false);
        var projects = map.Projects
            .Select(project => new ProjectInfo(project.Name, project.Path, project.DocumentCount))
            .ToArray();
        var frameworks = ProjectFileReader.TargetFrameworks(projects.Select(project => project.Path));
        return new ProjectModel(request.SolutionPath, projects, frameworks, diagnostics.ErrorCount, diagnostics.WarningCount, MapText(map));
    }

    private static string MapText(SolutionMap map)
    {
        var lines = new List<string>();
        foreach (var project in map.Projects)
        {
            lines.Add($"{project.Name} ({project.DocumentCount} docs)");
        }

        foreach (var reference in map.References)
        {
            lines.Add($"{reference.From} -> {reference.To}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
