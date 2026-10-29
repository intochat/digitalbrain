using System.Collections.Immutable;
using DigitalBrain.Analyzers.Reactions;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Apps;

/// <summary>
/// Every DigitalBrain analyzer, constructed directly (no loader). The single entry point for the os runtime script
/// compiler (<c>CompilationWithAnalyzers</c>, 08-06) so the fast path reports exactly what CI reports.
/// </summary>
public static class AnalyzerSet
{
    /// <summary>All analyzers: DB0001-DB0003, DB0013-DB0014 and DB0020-DB0028 (14 analyzers, one rule each).</summary>
    public static ImmutableArray<DiagnosticAnalyzer> All { get; } = ImmutableArray.Create<DiagnosticAnalyzer>(
        new ModuleShapeAnalyzer(),
        new AppSchedulingAnalyzer(),
        new OrleansAttributeAnalyzer(),
        new SignalLoopAnalyzer(),
        new ReactionShapeAnalyzer(),
        new AppBaseAnalyzer(),
        new VendorRequiresAnalyzer(),
        new EmptyCatchAnalyzer(),
        new KeyCompositionAnalyzer(),
        new SpecManifestAnalyzer(),
        new UntypedJsonAnalyzer(),
        new BaseBoilerplateAnalyzer(),
        new SpecCommentAnalyzer(),
        new SuppressionAnalyzer());
}
