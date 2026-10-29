using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Apps;

/// <summary>DB0020: a class marked <c>[App]</c> derives from <c>DigitalBrain.App</c>.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AppBaseAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(AppRules.AppBase);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!Rules.Has(type, Rules.AppAttribute) || Rules.DerivesFrom(type, Rules.AppType)) { return; }
        context.ReportDiagnostic(Diagnostic.Create(AppRules.AppBase, type.Locations[0], type.Name));
    }
}
