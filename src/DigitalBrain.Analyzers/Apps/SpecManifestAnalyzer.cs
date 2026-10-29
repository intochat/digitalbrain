using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DigitalBrain.Apps;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Apps;

/// <summary>
/// DB0024: the spec comment and the app disagree. Every <c>[On]</c> method of the app is described in the comment,
/// every operation the comment describes has an <c>[On]</c> method, and the header lines parse
/// (<c>Role</c> one of Shell/Default/Optional/Example, <c>Shipped</c> true/false). Uses the shared
/// <see cref="SpecComment"/> parser. A missing comment is DB0027, not this rule.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SpecManifestAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(AppRules.SpecMismatch);

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
        if (!AppRules.IsApp(type) || type.IsAbstract) { return; }
        var text = SpecSource.Find(type, context.Options, context.CancellationToken);
        if (text is null) { return; }
        var spec = SpecComment.Parse(text);

        foreach (var error in spec.Errors)
        {
            context.ReportDiagnostic(Diagnostic.Create(AppRules.SpecMismatch, type.Locations[0], type.Name, "the spec comment is malformed (" + error + ")"));
        }

        var handled = new HashSet<string>();
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            var onAttr = method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == Rules.OnAttribute);
            if (onAttr is null) { continue; }
            var isBackgroundSubscription = (onAttr.ConstructorArguments.Length > 0 && onAttr.ConstructorArguments[0].Value is string { Length: > 0 })
                || onAttr.NamedArguments.Any(arg => arg.Key == "Source" && arg.Value.Value is string { Length: > 0 });
            handled.Add(SpecComment.NormalizeOperation(method.Name));
            if (!isBackgroundSubscription && !spec.Describes(method.Name))
            {
                context.ReportDiagnostic(Diagnostic.Create(AppRules.SpecMismatch, method.Locations[0], type.Name,
                    "operation '" + method.Name + "' has an [On] method but is not described in the spec comment"));
            }
        }

        foreach (var operation in spec.Operations)
        {
            if (handled.Contains(operation)) { continue; }
            context.ReportDiagnostic(Diagnostic.Create(AppRules.SpecMismatch, type.Locations[0], type.Name,
                "operation '" + operation + "' is described in the spec comment but has no [On] method"));
        }
    }
}
