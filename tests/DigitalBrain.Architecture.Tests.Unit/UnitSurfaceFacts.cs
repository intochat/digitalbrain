using System.Reflection;
using DigitalBrain.Runtime.Synapses;

namespace DigitalBrain.Architecture.Tests.Unit;

// The `DigitalBrain` namespace is open to synapse units less AllowedApis.RefusedDigitalBrainTypes, so every public type
// there must be ruled: listed as unit-facing or refused. A new runtime type added to the namespace fails here until it is.
public sealed class UnitSurfaceFacts
{
    private static readonly string[] Shipped =
    [
        "DigitalBrain", "DigitalBrain.UI", "DigitalBrain.Testing", "DigitalBrain.Client", "DigitalBrain.Identity.Local",
        "DigitalBrain.Runtime.Http", "DigitalBrain.Runtime.Synapses",
        "DigitalBrain.Capabilities.Compute", "DigitalBrain.Capabilities.Connections", "DigitalBrain.Capabilities.Http",
        "DigitalBrain.Capabilities.Identity", "DigitalBrain.Capabilities.Secrets", "DigitalBrain.Capabilities.Storage",
        "DigitalBrain.Capabilities.Tools",
    ];

    [Fact]
    public void EveryPublicTypeInTheDigitalBrainNamespaceIsRuled()
    {
        var ruled = new HashSet<string>(AllowedApis.UnitFacingDigitalBrainTypes.Concat(AllowedApis.RefusedDigitalBrainTypes), StringComparer.Ordinal);

        var unruled = Shipped
            .Select(name => Assembly.Load(name))
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.DeclaringType is null && type.Namespace == "DigitalBrain")
            .Select(type => Name(type))
            .Where(name => !ruled.Contains(name))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(unruled.Count == 0, "Unruled public types in DigitalBrain (add each to AllowedApis.UnitFacingDigitalBrainTypes or RefusedDigitalBrainTypes): " + string.Join(", ", unruled));
    }

    // Generic arity dropped, as AllowedApis names types.
    private static string Name(Type type)
    {
        var name = type.FullName!;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick < 0 ? name : name[..tick];
    }
}
