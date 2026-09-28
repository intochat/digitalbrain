using Microsoft.CodeAnalysis;

namespace DigitalBrain.Microsoft.Roslyn;

public sealed record EditOutcome(
    Solution Changed,
    IReadOnlyList<DiagnosticHit> Diagnostics,
    string Diff,
    IReadOnlyList<string> ChangedPaths,
    int? FailingEdit,
    string? Detail)
{
    public bool HasErrors => FailingEdit is not null || Diagnostics.Any(static hit => hit.Severity == nameof(DiagnosticSeverity.Error));
}