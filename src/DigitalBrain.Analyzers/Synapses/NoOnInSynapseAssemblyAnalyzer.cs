using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Synapses;

/// <summary>DB0044: an <c>[On]</c> method in a compilation that declares a <c>[State]</c> record; a synapse assembly writes reactions as <c>[Synapse]</c> functions.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoOnInSynapseAssemblyAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules.NoOnInSynapseAssembly);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (!DeclaresState(start.Compilation.Assembly.GlobalNamespace)) { return; }
            start.RegisterSymbolAction(Analyze, SymbolKind.Method);
        });
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (!Rules.Has(method, Rules.OnAttribute)) { return; }

        var owner = method.ContainingType?.ToDisplayString();
        var name = owner is null ? method.Name : owner + "." + method.Name;
        context.ReportDiagnostic(Diagnostic.Create(Rules.NoOnInSynapseAssembly, method.Locations[0], name));
    }

    internal static bool DeclaresState(INamespaceOrTypeSymbol container)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamedTypeSymbol type && Rules.Has(type, Rules.StateAttribute)) { return true; }
            if (member is INamespaceOrTypeSymbol nested && DeclaresState(nested)) { return true; }
        }
        return false;
    }
}
