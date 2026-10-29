using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers;

/// <summary>DB0001: a concrete Module subclass carries [Module(id)] and a public static ModuleManifest Manifest.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ModuleShapeAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules.ModuleShape);

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
        if (type.TypeKind != TypeKind.Class || type.IsAbstract || !Rules.DerivesFrom(type, Rules.ModuleType)) { return; }

        if (!Rules.Has(type, Rules.ModuleAttribute))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rules.ModuleShape, type.Locations[0], type.Name, "carry [Module(id)]"));
        }
        var hasManifest = type.GetMembers("Manifest").OfType<IPropertySymbol>().Any(property =>
            property.IsStatic && property.DeclaredAccessibility == Accessibility.Public
            && Rules.FullName(property.Type as INamedTypeSymbol) == Rules.ManifestType);
        if (!hasManifest)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rules.ModuleShape, type.Locations[0], type.Name, "declare a public static ModuleManifest Manifest property"));
        }
    }
}

/// <summary>DB0002: neuron scheduling attributes are not allowed inside App classes.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AppSchedulingAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules.SchedulingInApp);

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
        if (type.TypeKind != TypeKind.Class || !(Rules.DerivesFrom(type, Rules.AppType) || Rules.Has(type, Rules.AppAttribute))) { return; }

        foreach (var symbol in new ISymbol[] { type }.Concat(type.GetMembers()))
        {
            foreach (var data in symbol.GetAttributes())
            {
                if (Rules.SchedulingAttributes.Contains(Rules.FullName(data.AttributeClass)))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rules.SchedulingInApp, Rules.LocationOf(data, symbol), data.AttributeClass!.Name, type.Name));
                }
            }
        }
    }
}

/// <summary>DB0003: Orleans attributes are banned on types that carry a DigitalBrain identity attribute.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OrleansAttributeAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules.OrleansOnIdentity);

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
        var attributes = type.GetAttributes();
        if (!attributes.Any(data => Rules.IdentityAttributes.Contains(Rules.FullName(data.AttributeClass)))) { return; }
        var orleans = attributes.FirstOrDefault(data => Rules.IsOrleans(data.AttributeClass));
        if (orleans is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rules.OrleansOnIdentity, type.Locations[0], type.Name, Rules.FullName(orleans.AttributeClass)));
        }
    }
}
