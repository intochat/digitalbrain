using System.Reflection;
using DigitalBrain.Capabilities;

namespace DigitalBrain.Runtime.Orleans.Capabilities;

/// <summary>A module that provides a capability but whose tests hold no concrete subclass of the capability's kit.</summary>
/// <param name="ModuleId">The module id.</param>
/// <param name="Module">The module class.</param>
/// <param name="Capability">The uncovered capability.</param>
public sealed record KitGap(string ModuleId, Type Module, CapabilityId Capability);

/// <summary>
/// The reflection check behind the kit-coverage fact (D-06, R4-04): every <c>Provides</c> entry of a module needs a non-abstract
/// subclass of the capability's conformance kit in that module's own test assembly. No test framework reference; host suites assert.
/// </summary>
public static class KitCoverage
{
    /// <summary>Every (module, capability) pair without a concrete kit subclass; capabilities with no kit base are not checked.</summary>
    /// <param name="moduleTypes">The module classes.</param>
    /// <param name="testAssemblyByModule">The test assembly built from each module's own tests folder; a missing entry is a gap.</param>
    /// <param name="kitBaseByCapability">The kit base per capability; open generics (<c>StorageConformance&lt;&gt;</c>) match any closure.</param>
    public static IReadOnlyList<KitGap> Missing(
        IEnumerable<Type> moduleTypes,
        IReadOnlyDictionary<Type, Assembly> testAssemblyByModule,
        IReadOnlyDictionary<CapabilityId, Type> kitBaseByCapability)
    {
        ArgumentNullException.ThrowIfNull(moduleTypes);
        ArgumentNullException.ThrowIfNull(testAssemblyByModule);
        ArgumentNullException.ThrowIfNull(kitBaseByCapability);
        var gaps = new List<KitGap>();
        foreach (var module in moduleTypes)
        {
            var manifest = ModuleManifest.Of(module);
            foreach (var capability in manifest.Provides)
            {
                if (!kitBaseByCapability.TryGetValue(capability, out var kit)) { continue; }
                if (!testAssemblyByModule.TryGetValue(module, out var tests) || !HasKitSubclass(LoadableTypes(tests), kit))
                {
                    gaps.Add(new KitGap(manifest.Id ?? module.Name, module, capability));
                }
            }
        }
        return gaps;
    }

    /// <summary>True when <paramref name="types"/> holds a non-abstract class deriving from <paramref name="kitBase"/>.</summary>
    public static bool HasKitSubclass(IEnumerable<Type> types, Type kitBase)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(kitBase);
        return types.Any(type => type.IsClass && !type.IsAbstract && DerivesFrom(type, kitBase));
    }

    private static bool DerivesFrom(Type type, Type kitBase)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current == kitBase || (kitBase.IsGenericTypeDefinition && current.IsGenericType && current.GetGenericTypeDefinition() == kitBase)) { return true; }
        }
        return false;
    }

    private static Type[] LoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException partial) { return [.. partial.Types.OfType<Type>()]; }
    }
}
