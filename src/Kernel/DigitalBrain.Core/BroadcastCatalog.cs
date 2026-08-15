using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

internal sealed class BroadcastCatalog
{
    private readonly ConcurrentDictionary<string, ImmutableHashSet<string>> _handlers =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string Alias, string GrainType), byte> _routes = new();

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

            if (AliasOf(neuronType, entry.Synapse) is { } alias)
            {
                _routes.TryAdd((alias, grainType), 0);
            }
        }
    }

    internal IReadOnlyCollection<string> HandlerGrainTypes(string synapseType)
        => _handlers.TryGetValue(synapseType, out var handlers) ? handlers : [];

    internal IReadOnlyCollection<BroadcastRoute> Routes()
        =>
        [
            .. _routes.Keys
                .Select(route => new BroadcastRoute(route.Alias, route.GrainType))
                .OrderBy(route => route.SynapseAlias, StringComparer.Ordinal)
                .ThenBy(route => route.HandlerGrainType, StringComparer.Ordinal),
        ];

    private static string? AliasOf(Type neuronType, string synapseTypeName)
    {
        foreach (var contract in neuronType.GetInterfaces())
        {
            if (contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IHandle<>)
                && contract.GenericTypeArguments[0].FullName == synapseTypeName)
            {
                return SynapseAlias.Of(contract.GenericTypeArguments[0]);
            }
        }

        return null;
    }
}

