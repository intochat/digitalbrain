using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions;
using Orleans;

using DigitalBrain.Abstractions.Signals;
namespace DigitalBrain.Core;

public static class SignalTypeIndex
{
    private static readonly ConcurrentDictionary<string, Type?> Resolved = new(StringComparer.Ordinal);

    public static Type? FindByAlias(string signalAlias)
    {
        if (string.IsNullOrWhiteSpace(signalAlias))
        {
            return null;
        }

        return Resolved.GetOrAdd(signalAlias, static alias =>
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
                        || !typeof(Signal).IsAssignableFrom(type)
                        || !string.Equals(SignalAlias.Of(type), alias, StringComparison.Ordinal))
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

