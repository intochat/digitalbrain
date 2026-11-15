using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Screens;

/// <summary>
/// DB0022: a catch block in a screen that is empty, discards, <c>return;</c>, or <c>continue;</c>,
/// or any broad swallow (<c>catch { }</c> / <c>catch (Exception ...)</c>) outside test projects.
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
        if (TestProjectExclusion.IsTestProject(context.Compilation, context.Options)) { return; }

        var clause = (CatchClauseSyntax)context.Node;
        if (!IsSwallowing(clause.Block.Statements)) { return; }

        var screen = ScreenRules.ContainingScreen(context.ContainingSymbol);
        if (screen is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(ScreenRules.SwallowingCatch, clause.CatchKeyword.GetLocation(), screen.Name));
            return;
        }

        if (!IsBroadCatch(clause, context.SemanticModel, context.CancellationToken)) { return; }

        var owner = context.ContainingSymbol?.ContainingType?.Name ?? context.ContainingSymbol?.Name ?? "unknown";
        context.ReportDiagnostic(Diagnostic.Create(ScreenRules.SwallowingCatch, clause.CatchKeyword.GetLocation(), owner));
    }

    private static bool IsSwallowing(SyntaxList<StatementSyntax> statements)
    {
        if (statements.Count == 0) { return true; }
        if (statements.Count == 1)
        {
            return statements[0] is ReturnStatementSyntax { Expression: null } or ContinueStatementSyntax
                || IsDiscardAssignment(statements[0]);
        }

        return statements.Count == 2
            && IsDiscardAssignment(statements[0])
            && statements[1] is ReturnStatementSyntax { Expression: null } or ContinueStatementSyntax;
    }

    private static bool IsDiscardAssignment(StatementSyntax statement)
        => statement is ExpressionStatementSyntax
        {
            Expression: AssignmentExpressionSyntax { Left: IdentifierNameSyntax { Identifier.Text: "_" } }
        };

    private static bool IsBroadCatch(CatchClauseSyntax clause, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        if (clause.Declaration is null) { return true; }
        var caughtType = semanticModel.GetTypeInfo(clause.Declaration.Type, cancellationToken).Type;
        var systemException = semanticModel.Compilation.GetTypeByMetadataName("System.Exception");
        return systemException is not null && SymbolEqualityComparer.Default.Equals(caughtType, systemException);
    }
}
