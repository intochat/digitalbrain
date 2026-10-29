using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Apps;

/// <summary>
/// DB0026: an app re-implements what the App base provides: a request ledger type (name ending in
/// <c>RequestLedger</c>), a <c>Key(brain)</c> method (named <c>Key</c>, one parameter, returns string) or a
/// <c>Present</c> wrapper method. Checked in app types and their nested types, and in static helper classes of a
/// compilation that declares an app (the shape the os apps use today).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BaseBoilerplateAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(AppRules.ReimplementedHelper);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var app = new Lazy<INamedTypeSymbol?>(() => FirstApp(start.Compilation.Assembly.GlobalNamespace));
            start.RegisterSymbolAction(symbol => AnalyzeType(symbol, app), SymbolKind.NamedType);
            start.RegisterSymbolAction(symbol => AnalyzeMethod(symbol, app), SymbolKind.Method);
        });
    }

    private static void AnalyzeType(SymbolAnalysisContext context, Lazy<INamedTypeSymbol?> firstApp)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!type.Name.EndsWith("RequestLedger", StringComparison.Ordinal)) { return; }
        // The App base ledger itself (DigitalBrain.RequestLedger) is what the rule points to, not a re-implementation.
        if (type.ContainingType is null && type.ContainingNamespace?.ToDisplayString() == "DigitalBrain") { return; }
        var app = AppRules.ContainingApp(type.ContainingType) ?? firstApp.Value;
        if (app is null) { return; }
        context.ReportDiagnostic(Diagnostic.Create(AppRules.ReimplementedHelper, type.Locations[0], app.Name, type.Name));
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context, Lazy<INamedTypeSymbol?> firstApp)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (method.MethodKind != MethodKind.Ordinary || method.IsOverride || method.ContainingType is not { } owner) { return; }
        var key = method.Name == "Key" && method.Parameters.Length == 1 && method.ReturnType.SpecialType == SpecialType.System_String;
        var present = method.Name == "Present";
        if (!key && !present) { return; }
        var app = AppRules.ContainingApp(owner) ?? (owner.IsStatic ? firstApp.Value : null);
        if (app is null) { return; }
        context.ReportDiagnostic(Diagnostic.Create(AppRules.ReimplementedHelper, method.Locations[0], app.Name, method.Name));
    }

    private static INamedTypeSymbol? FirstApp(INamespaceSymbol ns)
    {
        foreach (var member in ns.GetMembers())
        {
            if (member is INamespaceSymbol inner)
            {
                if (FirstApp(inner) is { } found) { return found; }
            }
            else if (member is INamedTypeSymbol type && AppRules.IsApp(type) && !type.IsAbstract)
            {
                return type;
            }
        }
        return null;
    }
}
