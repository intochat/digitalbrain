using System.Reflection;
using DigitalBrain.Capabilities;

namespace DigitalBrain.Runtime.Orleans.Capabilities;

/// <summary>
/// Refuses a module set at load (R4-02, D-05, D-09): a <c>Provides</c> entry naming an unknown capability id or major, a
/// <c>Provides</c> entry with no <c>[Neuron]</c> type in the module assembly assignable to the contract or an accepted binding,
/// and two modules providing the same single-connection capability. <c>Requires</c> is never checked here: a need is not a grant.
/// </summary>
public static class ModuleCapabilityValidator
{
    /// <summary>Validates module classes through <see cref="ModuleManifest.Of(Type)"/> and the <c>[Neuron]</c> types of their assemblies.</summary>
    /// <exception cref="InvalidOperationException">Any refusal; the message lists every one.</exception>
    public static void Validate(CapabilityRegistry registry, IEnumerable<Type> moduleTypes)
    {
        ArgumentNullException.ThrowIfNull(moduleTypes);
        Validate(registry, moduleTypes.Select(type =>
        {
            var manifest = ModuleManifest.Of(type);
            return new ModuleCandidate(manifest.Id ?? type.FullName ?? type.Name, manifest.Provides, NeuronTypes(type.Assembly));
        }));
    }

    internal static void Validate(CapabilityRegistry registry, IEnumerable<ModuleCandidate> modules)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(modules);
        var issues = new List<string>();
        var singleProviders = new Dictionary<CapabilityId, string>();
        foreach (var module in modules)
        {
            foreach (var capability in module.Provides)
            {
                if (!registry.Known(capability))
                {
                    issues.Add($"Module '{module.Id}' provides unknown capability {capability}.");
                    continue;
                }
                var bindings = registry.BindingsOf(capability);
                if (bindings.Count > 0 && !module.NeuronTypes.Any(neuron => bindings.Any(binding => binding.IsAssignableFrom(neuron))))
                {
                    issues.Add($"Module '{module.Id}' provides {capability} but contains no neuron implementing {registry.ContractOf(capability)?.Name}.");
                }
                if (registry.IsSingle(capability))
                {
                    if (singleProviders.TryGetValue(capability, out var first))
                    {
                        issues.Add($"Modules '{first}' and '{module.Id}' both provide {capability}, a single-connection capability.");
                    }
                    else
                    {
                        singleProviders[capability] = module.Id;
                    }
                }
            }
        }
        if (issues.Count > 0) { throw new InvalidOperationException(string.Join(Environment.NewLine, issues)); }
    }

    private static Type[] NeuronTypes(Assembly assembly)
    {
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException partial) { types = [.. partial.Types.OfType<Type>()]; }
        return [.. types.Where(type => type.IsClass && !type.IsAbstract && type.IsDefined(typeof(NeuronAttribute), inherit: false))];
    }
}

/// <summary>A module as the validator sees it: its id, its Provides and the neuron types of its assembly.</summary>
internal sealed record ModuleCandidate(string Id, IReadOnlyList<CapabilityId> Provides, IReadOnlyList<Type> NeuronTypes);
