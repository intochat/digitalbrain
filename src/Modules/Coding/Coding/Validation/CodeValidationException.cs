using System.Text.RegularExpressions;

namespace DigitalBrain.Coding;

internal sealed partial class CodeValidationException(string phase, string output) : Exception(phase + " failed.")
{
    public IReadOnlyList<CodeCheckDiagnostic> Diagnostics { get; } = Parse(phase, output);

    internal static IReadOnlyList<CodeCheckDiagnostic> Parse(string phase, string output)
    {
        var diagnostics = CompilerDiagnostic().Matches(output).Select(match => new CodeCheckDiagnostic(
            match.Groups[5].Value, match.Groups[4].Value, match.Groups[6].Value.Trim(),
            FileName(match.Groups[1].Value), int.Parse(match.Groups[2].Value),
            match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : null)).Distinct().Take(100).ToArray();
        return diagnostics.Length > 0 ? diagnostics : [new("validation", "error", phase + ": " + output[..Math.Min(output.Length, 16000)])];
    }

    private static string FileName(string path)
    {
        var separator = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        return separator < 0 ? path : path[(separator + 1)..];
    }

    [GeneratedRegex(@"^(.+?)\((\d+)(?:,(\d+))?\):\s*(error|warning)\s+(\w+):\s*(.*?)(?:\s+\[.*\])?$", RegexOptions.Multiline)]
    private static partial Regex CompilerDiagnostic();
}