using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Reactions;

/// <summary>DB0014: an [On] method takes one Signal-derived parameter plus an optional trailing CancellationToken and returns Task or ValueTask.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReactionShapeAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules.ReactionShape);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.Method);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (!Rules.Has(method, Rules.OnAttribute) || IsWellFormed(method)) { return; }
        context.ReportDiagnostic(Diagnostic.Create(Rules.ReactionShape, method.Locations[0], method.Name));
    }

    private static bool IsWellFormed(IMethodSymbol method)
    {
        if (Apps.AppRules.IsApp(method.ContainingType))
        {
            return IsWellFormedAppOperation(method);
        }

        var returns = Rules.FullName(method.ReturnType as INamedTypeSymbol);
        if (returns != "System.Threading.Tasks.Task" && returns != "System.Threading.Tasks.ValueTask") { return false; }

        var parameters = method.Parameters;
        if (parameters.Length is < 1 or > 2) { return false; }
        if (!Rules.IsOrDerivesFrom(parameters[0].Type, Rules.SignalType)) { return false; }
        return parameters.Length == 1 || Rules.FullName(parameters[1].Type as INamedTypeSymbol) == "System.Threading.CancellationToken";
    }

    private static bool IsWellFormedAppOperation(IMethodSymbol method)
    {
        if (method.IsStatic || method.IsGenericMethod) { return false; }
        var returns = Rules.FullName(method.ReturnType as INamedTypeSymbol);
        if (method.ReturnType.SpecialType != SpecialType.System_Void
            && returns != "System.Threading.Tasks.Task"
            && returns != "System.Threading.Tasks.ValueTask")
        {
            return false;
        }

        var parameters = method.Parameters;
        if (parameters.Length > 2) { return false; }
        if (parameters.Length == 2)
        {
            return Rules.FullName(parameters[1].Type as INamedTypeSymbol) == "System.Threading.CancellationToken"
                && Rules.FullName(parameters[0].Type as INamedTypeSymbol) != "System.Threading.CancellationToken";
        }
        return true;
    }
}
