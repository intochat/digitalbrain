using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Reactions;

/// <summary>DB0013 (CORE-06): no <c>await foreach</c> over a Signal stream inside a Neuron or App; reactions are [On] methods.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SignalLoopAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules.SignalLoop);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        // T-04-07: syntax-node registration; the semantic lookup runs only on await-foreach nodes.
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ForEachStatement, SyntaxKind.ForEachVariableStatement);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var loop = (CommonForEachStatementSyntax)context.Node;
        if (!loop.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword)) { return; }

        var type = context.ContainingSymbol?.ContainingType;
        if (type is null || !(Rules.DerivesFrom(type, Rules.NeuronType) || Rules.IsOrDerivesFrom(type.BaseType, Rules.AppType))) { return; }

        var info = context.SemanticModel.GetForEachStatementInfo(loop);
        if (!Rules.IsOrDerivesFrom(info.ElementType, Rules.SignalType)) { return; }

        context.ReportDiagnostic(Diagnostic.Create(Rules.SignalLoop, loop.GetLocation(), type.Name, info.ElementType!.Name));
    }
}
