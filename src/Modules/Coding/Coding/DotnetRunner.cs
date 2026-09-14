using System.Globalization;
using System.Text.RegularExpressions;

namespace DigitalBrain.Coding;

public sealed partial class DotnetRunner(IProcessRunner processes)
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(20);

    public async Task<BuildOutcome> BuildAsync(string solutionPath, string? artifactsPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        var arguments = new List<string> { "build", solutionPath, "-c", "Release", "--nologo" };
        if (!string.IsNullOrWhiteSpace(artifactsPath))
        {
            arguments.Add("-p:ArtifactsPath=" + artifactsPath);
        }

        var result = await processes.RunAsync("dotnet", arguments, Path.GetDirectoryName(Path.GetFullPath(solutionPath))!, BuildTimeout, cancellationToken).ConfigureAwait(false);
        var hits = ParseDiagnostics(result.Output + "\n" + result.Error);
        var errors = hits.Where(static hit => hit.Severity == "Error").ToArray();
        return new BuildOutcome(
            result.ExitCode == 0 && !result.TimedOut,
            errors,
            hits.Count(static hit => hit.Severity == "Warning"),
            result.Duration.TotalSeconds,
            Command(arguments),
            Detail(result, errors.Length == 0 && result.ExitCode != 0 ? "the build failed without a parsable error; see the output" : null));
    }

    public async Task<TestOutcome> TestAsync(string projectOrSolutionPath, string? filterClass, string? artifactsPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectOrSolutionPath);
        var arguments = new List<string> { "test", projectOrSolutionPath, "-c", "Release", "--no-build" };
        if (!string.IsNullOrWhiteSpace(artifactsPath))
        {
            arguments.Add("-p:ArtifactsPath=" + artifactsPath);
        }

        if (!string.IsNullOrWhiteSpace(filterClass))
        {
            arguments.AddRange(["--", "--filter-class", filterClass]);
        }

        var result = await processes.RunAsync("dotnet", arguments, Path.GetDirectoryName(Path.GetFullPath(projectOrSolutionPath))!, TestTimeout, cancellationToken).ConfigureAwait(false);
        var text = result.Output + "\n" + result.Error;
        var total = Count(text, "total");
        var failed = Count(text, "failed");
        var passed = Count(text, "succeeded");
        var skipped = Count(text, "skipped");
        var failures = FailedTestPattern().Matches(text)
            .Select(match => new TestFailure(match.Groups["name"].Value, match.Groups["message"].Value.Trim()))
            .ToArray();
        return new TestOutcome(
            result.ExitCode == 0 && !result.TimedOut && failed == 0,
            total, passed, failed, skipped, failures, result.Duration.TotalSeconds,
            Command(arguments),
            Detail(result, total == 0 ? "no test summary was found in the output" : null));
    }

    private static string Command(IReadOnlyList<string> arguments)
        => "dotnet " + string.Join(' ', arguments.Select(Quote));

    private static string Quote(string argument)
        => argument.Any(char.IsWhiteSpace) ? $"\"{argument}\"" : argument;

    private static string? Detail(ProcessResult result, string? fallback)
        => result.TimedOut ? "the process timed out" : fallback;

    private static int Count(string text, string label)
    {
        var match = SummaryPattern().Matches(text).LastOrDefault(match => match.Groups["label"].Value == label);
        return match is null ? 0 : int.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<DiagnosticHit> ParseDiagnostics(string text)
        => DiagnosticPattern().Matches(text)
            .Select(match => new DiagnosticHit(
                match.Groups["id"].Value,
                match.Groups["severity"].Value == "error" ? "Error" : "Warning",
                match.Groups["message"].Value.Trim(),
                match.Groups["path"].Value.Trim(),
                match.Groups["line"].Success ? int.Parse(match.Groups["line"].Value, CultureInfo.InvariantCulture) : 0))
            .DistinctBy(static hit => (hit.Id, hit.Path, hit.Line, hit.Message))
            .ToArray();

    // "E:\repo\src\A\Thing.cs(12,9): error CS0103: message [E:\repo\src\A\A.csproj]" and the project-level
    // "E:\repo\src\A\A.csproj : error NU1101: message [..]" form without a position.
    [GeneratedRegex(@"^\s*(?<path>[^\r\n(]+?)(?:\((?<line>\d+),\d+\))?\s*:\s*(?<severity>error|warning)\s+(?<id>[A-Z]+\d+):\s*(?<message>.*?)(?:\s\[[^\]]*\])?\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosticPattern();

    // Matches "label: count" wherever it appears, one label at a time - on its own line, or comma-joined
    // with the other labels on a single summary line ("total: 0, failed: 0, succeeded: 0, skipped: 0").
    [GeneratedRegex(@"(?<label>total|failed|succeeded|skipped):\s*(?<count>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex SummaryPattern();

    // "failed Namespace.Class.Test (12ms)" - the name is lazy so a [Theory] display name that itself
    // contains parentheses or spaces still stops at the trailing "(<duration>)". The message block that
    // follows keeps indented lines and blank lines, stopping at the first unindented, non-empty line.
    [GeneratedRegex(@"^failed (?<name>.+?) \([^)\r\n]*\)[ \t]*\r?\n(?<message>(?:(?:[ \t]+.*)?\r?\n)*)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex FailedTestPattern();
}
