using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DigitalBrain.Analyzers;

// Every id here is listed in AnalyzerReleases.Unshipped.md (RS2008). Attributes are matched by full metadata name only.
internal static class Rules
{
    private const string Category = "DigitalBrain";

    public static readonly DiagnosticDescriptor ModuleShape = new(
        "DB0001", "Module class needs [Module] and a public static ModuleManifest Manifest",
        "'{0}' derives from DigitalBrain.Module and must {1}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SchedulingInApp = new(
        "DB0002", "Scheduling attribute inside an App class",
        "'{0}' is a neuron scheduling attribute and cannot be used in app '{1}'; put it on a [Neuron] class",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OrleansOnIdentity = new(
        "DB0003", "Orleans attribute on a DigitalBrain identity type",
        "'{0}' carries the Orleans attribute '{1}'; Orleans attributes belong to the runtime, not to DigitalBrain types",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    // 04-03 (CORE-06): ids DB0013/DB0014 sit after Phase 8's reserved DB0001..DB0012 set.
    public static readonly DiagnosticDescriptor SignalLoop = new(
        "DB0013", "Signal loop inside a Neuron or App",
        "'{0}' loops over a {1} stream; use an [On] reaction and Context.Synapses.Ensure",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ReactionShape = new(
        "DB0014", "Malformed [On] reaction",
        "[On] method '{0}' must take exactly one Signal-derived parameter (plus an optional trailing CancellationToken) and return Task or ValueTask",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public const string SignalType = "DigitalBrain.Signal";
    public const string NeuronType = "DigitalBrain.Neuron";
    public const string OnAttribute = "DigitalBrain.OnAttribute";
    public const string ModuleAttribute = "DigitalBrain.ModuleAttribute";
    public const string AppAttribute = "DigitalBrain.AppAttribute";
    public const string ModuleType = "DigitalBrain.Module";
    public const string AppType = "DigitalBrain.App";
    public const string ManifestType = "DigitalBrain.ModuleManifest";

    public static readonly ImmutableHashSet<string> IdentityAttributes = ImmutableHashSet.Create(
        "DigitalBrain.NeuronAttribute", "DigitalBrain.SignalAttribute", AppAttribute, ModuleAttribute);

    public static readonly ImmutableHashSet<string> SchedulingAttributes = ImmutableHashSet.Create(
        "DigitalBrain.ReadOnlyCallAttribute", "DigitalBrain.OneWayCallAttribute", "DigitalBrain.InterleavesAttribute",
        "DigitalBrain.InterleavesWhenAttribute", "DigitalBrain.CallTimeoutAttribute", "DigitalBrain.ReentrantAttribute");

    public static string FullName(INamedTypeSymbol? type)
    {
        if (type is null) { return string.Empty; }
        return type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() + "." + type.MetadataName : type.MetadataName;
    }

    public static bool DerivesFrom(INamedTypeSymbol type, string fullName)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (FullName(current) == fullName) { return true; }
        }
        return false;
    }

    public static bool IsOrDerivesFrom(ITypeSymbol? type, string fullName)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (FullName(current.OriginalDefinition) == fullName) { return true; }
        }
        return false;
    }

    public static bool Has(ISymbol symbol, string attribute)
    {
        foreach (var data in symbol.GetAttributes())
        {
            if (FullName(data.AttributeClass) == attribute) { return true; }
        }
        return false;
    }

    public static Location LocationOf(AttributeData data, ISymbol fallback)
        => data.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? fallback.Locations[0];

    public static bool IsOrleans(INamedTypeSymbol? attribute)
    {
        var ns = attribute?.ContainingNamespace;
        if (ns is null || ns.IsGlobalNamespace) { return false; }
        while (!ns.ContainingNamespace.IsGlobalNamespace) { ns = ns.ContainingNamespace; }
        return ns.Name == "Orleans";
    }
}
