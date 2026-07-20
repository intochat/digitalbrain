using System.Reflection;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

public static class ModuleWiring
{
    public static IReadOnlyList<SynapseWiringEntry> HandlerWiring(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (!SynapseWiring.TryGetManifest(assembly, out var manifest))
        {
            return [];
        }

        return manifest.Handlers;
    }

    public static void EnsureManifestMatchesReflection(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (!SynapseWiring.TryGetManifest(assembly, out var manifest))
        {
            throw new ModuleCompositionException(
                $"Assembly '{assembly.GetName().Name}' has no generated dispatch manifest.");
        }

        var reflected = assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => SynapseWiring.HandledSynapseTypes(type)
                .Select(synapse => new SynapseWiringEntry(
                    type.FullName!,
                    synapse.FullName!)))
            .OrderBy(entry => entry.Neuron, StringComparer.Ordinal)
            .ThenBy(entry => entry.Synapse, StringComparer.Ordinal)
            .ToArray();

        var generated = manifest.Handlers
            .OrderBy(entry => entry.Neuron, StringComparer.Ordinal)
            .ThenBy(entry => entry.Synapse, StringComparer.Ordinal)
            .ToArray();

        if (!reflected.SequenceEqual(generated))
        {
            throw new ModuleCompositionException(
                $"Generated dispatch manifest for '{assembly.GetName().Name}' diverges from reflection.");
        }
    }
}
