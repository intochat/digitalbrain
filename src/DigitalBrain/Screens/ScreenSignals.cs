using System.Collections.Concurrent;
using System.Reflection;
using DigitalBrain.Neurons;

namespace DigitalBrain.Kernel;

/// <summary>The signals a screen class reacts to, mapped by journal alias.</summary>
internal static class ScreenSignals
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, Type>> ByAlias = new();

    /// <summary>Returns all signal types handled by <paramref name="screen"/>, mapped by alias.</summary>
    public static IReadOnlyDictionary<string, Type> For(Type screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        return screen.IsCollectible ? Scan(screen) : ByAlias.GetOrAdd(screen, Scan);
    }

    /// <summary>Finds the Signal type matching <paramref name="alias"/> for <paramref name="screen"/>.</summary>
    public static Type? Find(Type screen, string alias)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(alias);
        return For(screen).GetValueOrDefault(alias);
    }

    private static IReadOnlyDictionary<string, Type> Scan(Type screen)
    {
        var signals = new Dictionary<string, Type>(StringComparer.Ordinal);
        for (var type = screen; type is not null && type != typeof(object); type = type.BaseType)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.IsDefined(typeof(OnAttribute), false) && method.GetParameters() is [{ ParameterType: var signal }, ..]
                    && typeof(Signal).IsAssignableFrom(signal) && !signal.IsAbstract)
                {
                    signals.TryAdd(SignalAliases.Of(signal), signal);
                }
            }
        }

        return signals;
    }
}
