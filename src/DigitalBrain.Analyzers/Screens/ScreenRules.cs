using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DigitalBrain.Analyzers.Screens;

/// <summary>The screen rule descriptors DB0022, DB0025, DB0027, DB0028.</summary>
public static class ScreenRules
{
    public const string Category = "DigitalBrain.Screens";
    private const string Help = "https://github.com/intochat/handbook/blob/main/reference/app-convention.md#";

    public static readonly DiagnosticDescriptor SwallowingCatch = Create(
        "DB0022", "Empty or swallow-only catch",
        "A catch in screen '{0}' is empty or only swallows the error; rethrow or record the error state", DiagnosticSeverity.Error);

    public static readonly DiagnosticDescriptor HandWrittenJson = Create(
        "DB0025", "Hand-written JSON",
        "Screen '{0}' writes JSON by hand with '{1}'; use typed records", DiagnosticSeverity.Warning);

    public static readonly DiagnosticDescriptor MissingSpec = Create(
        "DB0027", "Missing spec comment",
        "Screen '{0}' has no leading spec comment and no sibling '{0}.spec.md'", DiagnosticSeverity.Error);

    public static readonly DiagnosticDescriptor Suppression = Create(
        "DB0028", "Suppressing a DigitalBrain rule",
        "Screen source may not suppress DigitalBrain rule '{0}'", DiagnosticSeverity.Error);

    public static readonly ImmutableArray<DiagnosticDescriptor> All = ImmutableArray.Create(
        SwallowingCatch, HandWrittenJson, MissingSpec, Suppression);

    public static DiagnosticDescriptor? ById(string id)
    {
        foreach (var descriptor in All)
        {
            if (descriptor.Id == id) { return descriptor; }
        }
        return null;
    }

    /// <summary>A class that derives from Screen&lt;TState&gt;.</summary>
    internal static bool IsScreen(INamedTypeSymbol? type)
    {
        if (type is not { TypeKind: TypeKind.Class }) { return false; }
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Name == "Screen" && (current.ContainingNamespace?.ToDisplayString() is "DigitalBrain" or "DigitalBrain.Screens"))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The innermost containing screen type of <paramref name="symbol"/>, or null.</summary>
    internal static INamedTypeSymbol? ContainingScreen(ISymbol? symbol)
    {
        for (var type = symbol as INamedTypeSymbol ?? symbol?.ContainingType; type is not null; type = type.ContainingType)
        {
            if (IsScreen(type)) { return type; }
        }
        return null;
    }

    private static DiagnosticDescriptor Create(string id, string title, string message, DiagnosticSeverity severity, params string[] customTags)
        => new(id, title, message, Category, severity, isEnabledByDefault: true, helpLinkUri: Help + id.ToLowerInvariant(), customTags: customTags);
}
