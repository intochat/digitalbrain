using System.Collections.Concurrent;
using System.Reflection;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

internal static class NeuronCatalog
{
    private static readonly ConcurrentDictionary<string, Type> SynapseTypes = new(StringComparer.Ordinal);

    internal static Type SynapseType(string name) => SynapseTypes.GetOrAdd(name, static requested =>
    {
        var matches = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(SafeTypes)
            .Where(type => type.IsSubclassOf(typeof(Synapse)) && !type.IsAbstract)
            .Where(type => string.Equals(type.Name, requested, StringComparison.Ordinal))
            .ToList();

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"No synapse type named '{requested}' is loaded."),
            _ => throw new InvalidOperationException(
                $"'{requested}' is ambiguous across {string.Join(", ", matches.Select(type => type.FullName))}. Rename one so scenarios address exactly one synapse."),
        };
    });

    internal static Synapse Create(string synapseTypeName, IReadOnlyDictionary<string, string> values)
    {
        var type = SynapseType(synapseTypeName);
        var constructor = type.GetConstructors().OrderByDescending(candidate => candidate.GetParameters().Length).First();

        var arguments = constructor.GetParameters()
            .Select(parameter => values.TryGetValue(parameter.Name!, out var raw)
                ? Convert.ChangeType(raw, parameter.ParameterType, System.Globalization.CultureInfo.InvariantCulture)
                : throw new InvalidOperationException($"Scenario did not supply a value for '{parameter.Name}' of {synapseTypeName}."))
            .ToArray();

        return (Synapse)constructor.Invoke(arguments);
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException loadFailure)
        {
            return loadFailure.Types.OfType<Type>();
        }
    }
}
