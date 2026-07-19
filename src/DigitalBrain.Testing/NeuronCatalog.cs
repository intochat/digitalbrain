using System.Collections.Concurrent;
using System.Reflection;

namespace DigitalBrain.Testing;

internal static class NeuronCatalog
{
    private static readonly ConcurrentDictionary<string, Type> SynapseTypes = new(StringComparer.Ordinal);

    internal static Type SynapseType(string name) => SynapseTypes.GetOrAdd(name, static requested =>
        AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(SafeTypes)
            .Where(type => type.IsSubclassOf(typeof(Synapse)) && !type.IsAbstract)
            .FirstOrDefault(type => string.Equals(type.Name, requested, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"No synapse type named '{requested}' is loaded."));

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
