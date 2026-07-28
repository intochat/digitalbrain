using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

internal sealed class BroadcastCatalog
{
    private readonly ConcurrentDictionary<string, ImmutableHashSet<string>> _handlers =
        new(StringComparer.Ordinal);

    internal void AddAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (!SynapseWiring.TryGetManifest(assembly, out var manifest))
        {
            return;
        }

        foreach (var entry in manifest.Handlers)
        {
            var neuronType = assembly.GetType(entry.Neuron)
                ?? throw new InvalidOperationException(
                    $"Dispatch manifest names neuron '{entry.Neuron}' but assembly '{assembly.GetName().Name}' does not define it.");

            var grainType = NeuronId.GrainTypeNameOf(neuronType);

            _handlers.AddOrUpdate(
                entry.Synapse,
                _ => ImmutableHashSet.Create(StringComparer.Ordinal, grainType),
                (_, existing) => existing.Add(grainType));
        }
    }

    internal IReadOnlyCollection<string> HandlerGrainTypes(string synapseType)
        => _handlers.TryGetValue(synapseType, out var handlers) ? handlers : [];
}
