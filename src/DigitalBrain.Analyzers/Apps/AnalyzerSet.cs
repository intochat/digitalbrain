using System.Collections.Immutable;
using DigitalBrain.Analyzers.Contracts;
using DigitalBrain.Analyzers.Neurons;
using DigitalBrain.Analyzers.Reactions;
using DigitalBrain.Analyzers.Synapses;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Apps;

/// <summary>
/// Every DigitalBrain analyzer, constructed directly (no loader). The single entry point for the os runtime script
/// compiler (<c>CompilationWithAnalyzers</c>, 08-06) so the fast path reports exactly what CI reports.
/// </summary>
public static class AnalyzerSet
{
    /// <summary>All analyzers: DB0001-DB0003, DB0013, DB0020-DB0023, DB0025-DB0028, DB0030-DB0033 (16 analyzers, one rule each).</summary>
    public static ImmutableArray<DiagnosticAnalyzer> All { get; } = ImmutableArray.Create<DiagnosticAnalyzer>(
        new ModuleShapeAnalyzer(),
        new AppSchedulingAnalyzer(),
        new OrleansAttributeAnalyzer(),
        new SignalLoopAnalyzer(),
        new AppBaseAnalyzer(),
        new VendorRequiresAnalyzer(),
        new EmptyCatchAnalyzer(),
        new KeyCompositionAnalyzer(),
        new UntypedJsonAnalyzer(),
        new BaseBoilerplateAnalyzer(),
        new SpecCommentAnalyzer(),
        new SuppressionAnalyzer(),
        new SingleAppClassAnalyzer(),
        new ShareableContractAnalyzer(),
        new ReentrancyJustificationAnalyzer(),
        new CallCycleAnalyzer());

    /// <summary>The synapse analyzers: DB0041-DB0044, applied by the synapse compiler on top of <see cref="All"/>.</summary>
    public static ImmutableArray<DiagnosticAnalyzer> Synapses { get; } = ImmutableArray.Create<DiagnosticAnalyzer>(
        new SynapseShapeAnalyzer(),
        new StateLocalityAnalyzer(),
        new StatusStringAnalyzer(),
        new NoOnInSynapseAssemblyAnalyzer());
}
