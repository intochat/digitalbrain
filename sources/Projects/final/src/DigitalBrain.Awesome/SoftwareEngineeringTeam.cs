namespace DigitalBrain.Awesome;

using DigitalBrain.Protocol;
using DigitalBrain.Os;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;
using DigitalBrain.Os.Infrastructure.Orleans;
using DigitalBrain.Os.UI;
using System.Text;

// Software engineering domain. One team neuron contributing design/review/implement capabilities via neuron/synapse.
// Declares IHandle<> (including domain-specific ReviewRequest) so it participates in installs and timeline.
public interface IAwesomeSoftwareEngineeringTeam : INeuron, IHandle<BundleInstalled>, IHandle<ReviewRequest>, IHandle<ReviewProjectRequest>;

[GrainType("awesome-se-team")]
public sealed class SoftwareEngineeringTeamNeuron : Neuron, IAwesomeSoftwareEngineeringTeam
{
    public Task HandleAsync(BundleInstalled synapse, CancellationToken cancellationToken)
    {
        // On bundle install, the SE team becomes ready and emits readiness telemetry.
        return Emit(new NeuronTelemetry(Self, "SETeamReadyForReviews", new Dictionary<string, string> { ["status"] = "ready" }));
    }

    public Task HandleAsync(ReviewRequest request, CancellationToken cancellationToken)
    {
        // Real SE job (software engineering): perform lightweight review (heuristic on content length + keywords) and emit typed ReviewResult.
        // This is the concrete behavior for the awesome domain.
        var content = request.DiffOrContent;
        var issues = content.Contains("TODO") ? 1 : 0;
        var summary = $"Reviewed {request.Target}. Length={content.Length}. Issues found: {issues}. Suggestion: " + (issues > 0 ? "address TODOs" : "looks good, consider adding tests");
        var result = new ReviewResult(request.Target, summary, DateTimeOffset.UtcNow);
        return Emit(result);
    }

    public async Task HandleAsync(ReviewProjectRequest request, CancellationToken cancellationToken)
    {
        var review = ProjectReview.Analyze(request.Path);
        await Emit(new ReviewResult(request.Path, review.Summary, DateTimeOffset.UtcNow));
        await Emit(new UiSurface($"review:{request.Path}", Self, new Markdown(review.Report)));
        await Emit(new NeuronTelemetry(Self, "ProjectReviewed", new Dictionary<string, string>
        {
            ["path"] = request.Path,
            ["files"] = review.FileCount.ToString(),
            ["todos"] = review.TodoCount.ToString(),
            ["truncated"] = review.Truncated.ToString()
        }));
    }
}

// Domain-specific synapses for the software engineering experience.
[GenerateSerializer]
public sealed record ReviewRequest(string Target, string DiffOrContent) : Synapse;

[GenerateSerializer]
public sealed record ReviewResult(string Target, string Summary, DateTimeOffset ReviewedAt) : Synapse;

[GenerateSerializer]
public sealed record ReviewProjectRequest(string Path) : Synapse;

// Use via: new InstallBundle(AwesomeExperiences.SoftwareEngineeringTeam) or the string InstallBundleAsync("awesome-se-team").
// The typed id + IHandle<> wiring makes this domain installable and discoverable as a distributable bundle.
public static class AwesomeExperiences
{
    public static readonly BundleId SoftwareEngineeringTeam = "awesome-se-team";
}

internal sealed record ProjectReviewOutcome(string Summary, string Report, int FileCount, int TodoCount, bool Truncated);

internal static class ProjectReview
{
    private const int MaxFiles = 100;
    private const long MaxTotalBytes = 1_000_000;

    public static ProjectReviewOutcome Analyze(string path)
    {
        if (!Directory.Exists(path))
        {
            var missing = $"Path '{path}' does not exist on the kernel machine.";
            return new ProjectReviewOutcome(missing, $"# Review failed\n\n{missing}\n\nThe kernel resolves paths locally — the path must be valid where the kernel runs, not where the client runs.", 0, 0, false);
        }

        var sep = Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
            .Take(MaxFiles + 1)
            .ToList();

        var truncated = files.Count > MaxFiles;
        if (truncated) files.RemoveAt(MaxFiles);

        long totalBytes = 0;
        var totalLines = 0;
        var todoCount = 0;
        var flagged = new List<(string File, int Todos, int Lines)>();
        var largest = new List<(string File, int Lines)>();

        foreach (var file in files)
        {
            var length = new FileInfo(file).Length;
            if (totalBytes + length > MaxTotalBytes)
            {
                truncated = true;
                break;
            }
            totalBytes += length;
            var lines = File.ReadAllLines(file);
            var todos = lines.Count(l => l.Contains("TODO", StringComparison.Ordinal));
            totalLines += lines.Length;
            todoCount += todos;
            var rel = Path.GetRelativePath(path, file);
            if (todos > 0) flagged.Add((rel, todos, lines.Length));
            largest.Add((rel, lines.Length));
        }

        var analyzed = largest.Count;
        var summary = $"Reviewed {analyzed} C# files ({totalLines} lines) at {path}. TODOs: {todoCount}." +
            (truncated ? $" Truncated at {MaxFiles} files / {MaxTotalBytes / 1000} KB." : "");

        var report = new StringBuilder()
            .AppendLine($"# Review: {path}")
            .AppendLine()
            .AppendLine($"**{analyzed} files**, **{totalLines} lines**, **{todoCount} TODOs**" + (truncated ? " *(truncated by caps)*" : ""))
            .AppendLine();

        if (flagged.Count > 0)
        {
            report.AppendLine("## Files with TODOs").AppendLine();
            foreach (var (file, todos, lines) in flagged.OrderByDescending(f => f.Todos).Take(10))
                report.AppendLine($"- `{file}` — {todos} TODO(s), {lines} lines");
            report.AppendLine();
        }

        report.AppendLine("## Largest files").AppendLine();
        foreach (var (file, lines) in largest.OrderByDescending(f => f.Lines).Take(5))
            report.AppendLine($"- `{file}` — {lines} lines");

        report.AppendLine().AppendLine(todoCount > 0
            ? "Suggestion: address the flagged TODOs, largest files first."
            : "Suggestion: no TODOs found; consider expanding test coverage.");

        return new ProjectReviewOutcome(summary, report.ToString(), analyzed, todoCount, truncated);
    }
}
