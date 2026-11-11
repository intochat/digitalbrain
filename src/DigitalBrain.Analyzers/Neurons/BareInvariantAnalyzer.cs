using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Neurons;

/// <summary>
/// DB0040 (opt-in): <c>throw new InvalidOperationException("...")</c> (or ArgumentException, InvalidDataException) with a message in a
/// public or [On] method of a neuron or app states an invariant that belongs in a Rule judged by Check. Guard calls (ThrowIf*),
/// rethrows inside catch, static and [Scenario] methods (verification, not behavior), UnauthorizedAccessException and nameof-only ArgumentException are not invariants.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BareInvariantAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> Invariants = ImmutableHashSet.Create(
        "System.InvalidOperationException", "System.ArgumentException", "System.IO.InvalidDataException");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules.BareInvariant);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ThrowStatement, SyntaxKind.ThrowExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var thrown = context.Node switch
        {
            ThrowStatementSyntax statement => statement.Expression,
            ThrowExpressionSyntax expression => expression.Expression,
            _ => null,
        };
        if (thrown is not BaseObjectCreationExpressionSyntax creation || creation.ArgumentList is not { Arguments.Count: > 0 } arguments) { return; }
        if (!IsMessage(arguments.Arguments[0].Expression)) { return; }
        if (context.Node.FirstAncestorOrSelf<CatchClauseSyntax>() is not null) { return; }
        if (context.Node.FirstAncestorOrSelf<MethodDeclarationSyntax>() is not { } declaration) { return; }
        if (context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type is not INamedTypeSymbol exception
            || !Invariants.Contains(Rules.FullName(exception))) { return; }
        if (context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not { } method) { return; }
        if (method.DeclaredAccessibility != Accessibility.Public && !Rules.Has(method, "DigitalBrain.OnAttribute")) { return; }
        if (method.IsStatic || Rules.Has(method, "DigitalBrain.ScenarioAttribute")) { return; }
        var owner = method.ContainingType;
        if (!Rules.DerivesFrom(owner, "DigitalBrain.Neuron")) { return; }
        context.ReportDiagnostic(Diagnostic.Create(Rules.BareInvariant, context.Node.GetLocation(), method.Name, exception.Name));
    }

    internal static bool IsMessage(ExpressionSyntax expression)
        => expression is InterpolatedStringExpressionSyntax
            || expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression);
}
