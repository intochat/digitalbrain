using System.Text;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Microsoft.Roslyn;

namespace DigitalBrain.CSharpExpert;

public sealed class UnderstandSolution(IDigitalBrain brain, string runId) : IBehavior, IBehaviorSignals
{
    public IReadOnlyList<Type> Signals => [typeof(FeatureRequested)];

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
        await RoslynWorkspace.OpenAsync(roslyn, request.SolutionPath, cancellation).ConfigureAwait(false);
        var map = await roslyn.Map(new MapQuery(), cancellation).ConfigureAwait(false);
        var diagnostics = await roslyn.Diagnostics(new DiagnosticsQuery(), cancellation).ConfigureAwait(false);
        var projects = map.Projects
            .Select(project => new ProjectInfo(project.Name, project.Path, project.DocumentCount))
            .ToArray();
        var frameworks = ProjectFileReader.TargetFrameworks(projects.Select(project => project.Path));
        var mapText = await MapTextAsync(roslyn, request.SolutionPath, map, cancellation).ConfigureAwait(false);
        return new ProjectModel(request.SolutionPath, projects, frameworks, diagnostics.ErrorCount, diagnostics.WarningCount, mapText);
    }

    private const int MaxMappedFiles = 80;
    private const int MaxMapCharacters = 30_000;

    private static async Task<string> MapTextAsync(IRoslyn roslyn, string solutionPath, SolutionMap map, CancellationToken cancellation)
    {
        var solutionFolder = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
        var text = new StringBuilder();
        foreach (var reference in map.References)
        {
            text.AppendLine($"{reference.From} -> {reference.To}");
        }

        var mappedFiles = 0;
        foreach (var project in map.Projects)
        {
            text.AppendLine($"Project {project.Name}");
            foreach (var file in SourceFiles(project.Path))
            {
                if (++mappedFiles > MaxMappedFiles || text.Length > MaxMapCharacters)
                {
                    text.AppendLine("  (more files omitted)");
                    return text.ToString();
                }

                text.AppendLine($"  {Path.GetRelativePath(solutionFolder, file).Replace('\\', '/')}");
                var skeleton = await roslyn.Skeleton(new SkeletonQuery(file), cancellation).ConfigureAwait(false);
                foreach (var member in skeleton.Members)
                {
                    text.AppendLine($"    {new string(' ', member.Depth * 2)}{member.Signature}  [{member.Id}]");
                }
            }
        }

        return text.ToString();
    }

    private static IEnumerable<string> SourceFiles(string projectPath)
    {
        var projectFolder = Path.GetDirectoryName(projectPath)!;
        return Directory.EnumerateFiles(projectFolder, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(projectFolder, file).Split(Path.DirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj"))
            .Order(StringComparer.OrdinalIgnoreCase);
    }
}
