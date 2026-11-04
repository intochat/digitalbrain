using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Synapses;

/// <summary>DB0042: a <c>[Synapse]</c> function's state record is declared in the same assembly as the function.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StateLocalityAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules.StateLocality);

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
        if (!Rules.Has(method, Rules.SynapseAttribute)) { return; }

        foreach (var parameter in method.Parameters)
        {
            if (parameter.Type is not INamedTypeSymbol type || !Rules.Has(type, Rules.StateAttribute)) { continue; }
            if (SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, method.ContainingAssembly)) { continue; }

            var owner = method.ContainingType?.ToDisplayString();
            var name = owner is null ? method.Name : owner + "." + method.Name;
            context.ReportDiagnostic(Diagnostic.Create(Rules.StateLocality, method.Locations[0], name, type.ToDisplayString()));
        }
    }
}
