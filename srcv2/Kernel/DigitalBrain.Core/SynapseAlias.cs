using System.Collections.Concurrent;
using System.Reflection;
using Orleans;

namespace DigitalBrain.Core;

public static class SynapseAlias
{
    private static readonly ConcurrentDictionary<Type, string?> Resolved = new();

    public static string? Of(Type synapseType)
    {
        ArgumentNullException.ThrowIfNull(synapseType);
        return Resolved.GetOrAdd(synapseType, static type =>
        {
            string? single = null;

            foreach (var attribute in type.GetCustomAttributes<AliasAttribute>(inherit: false))
            {
                if (string.IsNullOrWhiteSpace(attribute.Alias))
                {
                    continue;
                }

                if (single is not null)
                {
                    return null;
                }

                single = attribute.Alias;
            }

            return single;
        });
    }
}
