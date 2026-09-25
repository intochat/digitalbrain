using DigitalBrain.Contracts;
using DigitalBrain.Microsoft.DotNet;
using DigitalBrain.Microsoft.Roslyn;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.diagnostic-group")]
public sealed record DiagnosticGroup(
    [property: Id(0)] string Id,
    [property: Id(1)] string File,
    [property: Id(2)] IReadOnlyList<string> Messages);

public static class DiagnosticGroups
{
    public static IReadOnlyList<DiagnosticGroup> From(IEnumerable<BuildDiagnostic> diagnostics)
        => Group(diagnostics.Select(hit => (hit.Id, hit.Path, hit.Message)));

    public static IReadOnlyList<DiagnosticGroup> From(IEnumerable<DiagnosticHit> diagnostics)
        => Group(diagnostics.Select(hit => (hit.Id, hit.Path, hit.Message)));

    private static IReadOnlyList<DiagnosticGroup> Group(IEnumerable<(string Id, string File, string Message)> diagnostics)
        => [.. diagnostics
            .GroupBy(hit => (hit.Id, hit.File))
            .Select(group => new DiagnosticGroup(group.Key.Id, group.Key.File, [.. group.Select(hit => hit.Message).Distinct(StringComparer.Ordinal)]))
            .OrderBy(group => group.File, StringComparer.Ordinal)
            .ThenBy(group => group.Id, StringComparer.Ordinal)];
}
