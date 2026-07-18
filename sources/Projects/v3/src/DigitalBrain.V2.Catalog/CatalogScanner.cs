using System.Reflection;
using DigitalBrain.V2.Core.Runtime;
using DigitalBrain.V2.Core.Synapses;

namespace DigitalBrain.V2.Catalog;

public static class CatalogScanner
{
    public static CatalogDocument Scan(params Assembly[] assemblies)
    {
        var types = assemblies
            .SelectMany(LoadableTypes)
            .Where(type => type.FullName is not null)
            .ToArray();

        var synapses = types
            .Where(type => type is { IsAbstract: false } && typeof(Synapse).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => new CatalogEntry(
                type.FullName!,
                CatalogKind.Synapse,
                Fields(type),
                [],
                []))
            .ToArray();

        var neurons = types
            .Where(IsNeuronContract)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(NeuronContract.From)
            .ToArray();

        var neuronEntries = neurons
            .Select(neuron => new CatalogEntry(
                neuron.Type.FullName!,
                CatalogKind.Neuron,
                [],
                neuron.InEdges.Select(Fqn).Order(StringComparer.Ordinal).ToArray(),
                neuron.OutEdges.Select(Fqn).Order(StringComparer.Ordinal).ToArray()))
            .ToArray();

        return new CatalogDocument(
            [.. synapses, .. neuronEntries],
            Edges(neurons));
    }

    private static CatalogEdge[] Edges(NeuronContract[] neurons)
    {
        var edges = new List<CatalogEdge>();
        foreach (var source in neurons)
        {
            foreach (var emitted in source.OutEdges.OrderBy(type => type.FullName, StringComparer.Ordinal))
            {
                var targets = neurons
                    .Where(target => target.InEdges.Contains(emitted))
                    .OrderBy(target => target.Name, StringComparer.Ordinal)
                    .ToArray();

                if (targets.Length == 0)
                {
                    edges.Add(new CatalogEdge(source.Name, emitted.Name, Fqn(emitted), "*"));
                    continue;
                }

                edges.AddRange(targets.Select(target =>
                    new CatalogEdge(source.Name, emitted.Name, Fqn(emitted), target.Name)));
            }
        }

        return edges
            .OrderBy(edge => edge.From, StringComparer.Ordinal)
            .ThenBy(edge => edge.Synapse, StringComparer.Ordinal)
            .ThenBy(edge => edge.To, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsNeuronContract(Type type) =>
        type.IsInterface
        && type != typeof(INeuron)
        && type.Name.StartsWith('I')
        && type.Name.EndsWith("Neuron", StringComparison.Ordinal)
        && typeof(INeuron).IsAssignableFrom(type);

    private static Type[] EdgeTypes(Type type, Type marker) =>
        type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == marker)
            .Select(i => i.GetGenericArguments()[0])
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToArray();

    private static string[] Fields(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static Type[] LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }
    }

    private static string Fqn(Type type) => type.FullName ?? type.Name;

    private sealed record NeuronContract(Type Type, Type[] InEdges, Type[] OutEdges)
    {
        public string Name => ContractName(Type);

        public static NeuronContract From(Type type) =>
            new(type, EdgeTypes(type, typeof(IHandle<>)), EdgeTypes(type, typeof(IEmit<>)));
    }

    private static string ContractName(Type type)
    {
        var name = type.Name;
        if (name.StartsWith('I')) name = name[1..];
        return name.EndsWith("Neuron", StringComparison.Ordinal)
            ? name[..^"Neuron".Length]
            : name;
    }
}
