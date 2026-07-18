using DigitalBrain.InoLang.Diagnostics;

namespace DigitalBrain.InoLang.Tests;

public sealed record InoFileReport(
    string RelativePath,
    IReadOnlyList<Diagnostic> CompileDiagnostics,
    ScenarioReport? Scenarios)
{
    public bool Passed
        => !CompileDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)
            && Scenarios is { AllPassed: true };
}

public sealed record DirectoryScenarioReport(IReadOnlyList<InoFileReport> Files)
{
    // v3 §L6: empty tree ≠ green.
    public bool AllPassed => Files.Count > 0 && Files.All(f => f.Passed);
}
