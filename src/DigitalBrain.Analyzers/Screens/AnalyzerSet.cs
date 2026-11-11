using System.Collections.Immutable;
using DigitalBrain.Analyzers.Contracts;
using DigitalBrain.Analyzers.Neurons;
using DigitalBrain.Analyzers.Reactions;
using DigitalBrain.Analyzers.Synapses;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Screens;

/// <summary>
/// Every DigitalBrain analyzer, constructed directly (no loader). The single entry point for the os runtime script
/// compiler (<c>CompilationWithAnalyzers</c>) so the fast path reports exactly what CI reports.
/// </summary>
public static class AnalyzerSet
{
    /// <summary>All analyzers: DB0001, DB0003, DB0013, DB0022, DB0025, DB0027, DB0028, DB0031-DB0033 (10 analyzers, one rule each).</summary>
    public static ImmutableArray<DiagnosticAnalyzer> All { get; } = ImmutableArray.Create<DiagnosticAnalyzer>(
        new ModuleShapeAnalyzer(),
        new OrleansAttributeAnalyzer(),
        new SignalLoopAnalyzer(),
        new EmptyCatchAnalyzer(),
        new UntypedJsonAnalyzer(),
        new SpecCommentAnalyzer(),
        new SuppressionAnalyzer(),
        new ShareableContractAnalyzer(),
        new ReentrancyJustificationAnalyzer(),
        new CallCycleAnalyzer());

    /// <summary>The synapse analyzers: DB0041-DB0045, applied by the synapse compiler on top of <see cref="All"/>.</summary>
    public static ImmutableArray<DiagnosticAnalyzer> Synapses { get; } = ImmutableArray.Create<DiagnosticAnalyzer>(
        new SynapseShapeAnalyzer(),
        new StateLocalityAnalyzer(),
        new StatusStringAnalyzer(),
        new NoOnInSynapseAssemblyAnalyzer(),
        new BannedApiAnalyzer());
}
