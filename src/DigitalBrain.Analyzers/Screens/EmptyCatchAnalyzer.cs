using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Screens;

/// <summary>
/// DB0022: inside a screen, a catch block that is empty or whose only statement is <c>return;</c> or <c>continue;</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EmptyCatchAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(ScreenRules.SwallowingCatch);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.CatchClause);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var clause = (CatchClauseSyntax)context.Node;
        var statements = clause.Block.Statements;
        var swallows = statements.Count == 0
            || (statements.Count == 1 && statements[0] is ReturnStatementSyntax { Expression: null } or ContinueStatementSyntax);
        if (!swallows) { return; }
        var screen = ScreenRules.ContainingScreen(context.ContainingSymbol);
        if (screen is null) { return; }
        context.ReportDiagnostic(Diagnostic.Create(ScreenRules.SwallowingCatch, clause.CatchKeyword.GetLocation(), screen.Name));
    }
}
