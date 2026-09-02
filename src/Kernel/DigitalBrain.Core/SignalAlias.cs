using System.Collections.Concurrent;
using System.Reflection;
using Orleans;

namespace DigitalBrain.Core;

public static class SignalAlias
{
    private static readonly ConcurrentDictionary<Type, string?> Resolved = new();

    public static string? Of(Type signalType)
    {
        ArgumentNullException.ThrowIfNull(signalType);
        return Resolved.GetOrAdd(signalType, static type =>
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
