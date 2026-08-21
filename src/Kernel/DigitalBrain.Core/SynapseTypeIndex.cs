using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions;
using Orleans;

using DigitalBrain.Abstractions.Messaging;
namespace DigitalBrain.Core;

public static class SynapseTypeIndex
{
    private static readonly ConcurrentDictionary<string, Type?> Resolved = new(StringComparer.Ordinal);

    public static Type? FindByAlias(string synapseAlias)
    {
        if (string.IsNullOrWhiteSpace(synapseAlias))
        {
            return null;
        }

        return Resolved.GetOrAdd(synapseAlias, static alias =>
        {
            Type? found = null;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic
                    || assembly.GetName().Name is not { } name
                    || !name.StartsWith("DigitalBrain", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var type in SafeTypes(assembly))
                {
                    if (type is not { IsClass: true, IsAbstract: false }
                        || !typeof(Synapse).IsAssignableFrom(type)
                        || !string.Equals(SynapseAlias.Of(type), alias, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (found is not null && found != type)
                    {
                        return null;
                    }

                    found = type;
                }
            }

            return found;
        });
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partial)
        {
            return partial.Types.OfType<Type>();
        }
    }
}

