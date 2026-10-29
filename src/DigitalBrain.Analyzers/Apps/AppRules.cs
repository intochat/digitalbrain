using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DigitalBrain.Analyzers.Apps;

// Phase 8 app rules (08-CONTEXT R4-06 final id map). All nine ids are declared here once so they are reserved;
// DB0024..DB0026 implemented in 08-03. Messages carry symbol names, never literal values (T-08-03).
/// <summary>The app rule descriptors DB0020..DB0028 (handbook app-convention), one table for CI and the runtime fast path.</summary>
public static class AppRules
{
    /// <summary>The diagnostic category of every app rule.</summary>
    public const string Category = "DigitalBrain.Apps";
    private const string Help = "https://github.com/intochat/handbook/blob/main/reference/app-convention.md#";

    /// <summary>The AppBase rule.</summary>
    public static readonly DiagnosticDescriptor AppBase = Create(
        "DB0020", "An [App] class must derive from App",
        "'{0}' is marked [App] and must derive from DigitalBrain.App", DiagnosticSeverity.Error);

    /// <summary>The ModuleRequirement rule.</summary>
    public static readonly DiagnosticDescriptor ModuleRequirement = Create(
        "DB0021", "Requires names a module instead of a capability",
        "App '{0}' requires the module id '{1}'; require a capability (name@major) and let connections choose the module", DiagnosticSeverity.Error);

    /// <summary>The SwallowingCatch rule.</summary>
    public static readonly DiagnosticDescriptor SwallowingCatch = Create(
        "DB0022", "Empty or swallow-only catch",
        "A catch in app '{0}' is empty or only swallows the error; rethrow or record the error state", DiagnosticSeverity.Error);

    /// <summary>The StringKey rule.</summary>
    public static readonly DiagnosticDescriptor StringKey = Create(
        "DB0023", "Key composed from strings",
        "App '{0}' composes a key from '{1}' with strings; use the typed key parts of the App base", DiagnosticSeverity.Error);

    /// <summary>The SpecMismatch rule.</summary>
    public static readonly DiagnosticDescriptor SpecMismatch = Create(
        "DB0024", "Spec comment and manifest disagree",
        "App '{0}': {1}", DiagnosticSeverity.Warning);

    /// <summary>The HandWrittenJson rule.</summary>
    public static readonly DiagnosticDescriptor HandWrittenJson = Create(
        "DB0025", "Hand-written JSON",
        "App '{0}' writes JSON by hand with '{1}'; use typed records", DiagnosticSeverity.Warning);

    /// <summary>The ReimplementedHelper rule.</summary>
    public static readonly DiagnosticDescriptor ReimplementedHelper = Create(
        "DB0026", "Re-implemented App base helper",
        "App '{0}' re-implements '{1}'; delete it and use the App base member", DiagnosticSeverity.Warning);

    /// <summary>The MissingSpec rule.</summary>
    public static readonly DiagnosticDescriptor MissingSpec = Create(
        "DB0027", "Missing spec comment",
        "App '{0}' has no leading spec comment and no sibling '{0}.spec.md'", DiagnosticSeverity.Error);

    /// <summary>The Suppression rule.</summary>
    public static readonly DiagnosticDescriptor Suppression = Create(
        "DB0028", "Suppressing a DigitalBrain rule",
        "App source may not suppress DigitalBrain rule '{0}'", DiagnosticSeverity.Error);

    /// <summary>All nine app rules in id order.</summary>
    public static readonly ImmutableArray<DiagnosticDescriptor> All = ImmutableArray.Create(
        AppBase, ModuleRequirement, SwallowingCatch, StringKey, SpecMismatch, HandWrittenJson, ReimplementedHelper, MissingSpec, Suppression);

    /// <summary>The app rule with <paramref name="id"/>, or null.</summary>
    public static DiagnosticDescriptor? ById(string id)
    {
        foreach (var descriptor in All)
        {
            if (descriptor.Id == id) { return descriptor; }
        }
        return null;
    }

    /// <summary>A class that derives from DigitalBrain.App or carries [App].</summary>
    internal static bool IsApp(INamedTypeSymbol? type)
        => type is { TypeKind: TypeKind.Class } && (Rules.DerivesFrom(type, Rules.AppType) || Rules.Has(type, Rules.AppAttribute));

    /// <summary>The innermost containing app type of <paramref name="symbol"/>, or null.</summary>
    internal static INamedTypeSymbol? ContainingApp(ISymbol? symbol)
    {
        for (var type = symbol as INamedTypeSymbol ?? symbol?.ContainingType; type is not null; type = type.ContainingType)
        {
            if (IsApp(type)) { return type; }
        }
        return null;
    }

    private static DiagnosticDescriptor Create(string id, string title, string message, DiagnosticSeverity severity)
        => new(id, title, message, Category, severity, isEnabledByDefault: true, helpLinkUri: Help + id.ToLowerInvariant());
}
