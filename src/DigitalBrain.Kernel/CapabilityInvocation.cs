using System.Reflection;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

internal static class CapabilityInvocation
{
    private static readonly HashSet<Type> FrameworkInterfaces =
    [
        typeof(INeuron),
        typeof(ISessionNeuron),
        typeof(ISubscriptionRegistry),
        typeof(IJournalObserver),
    ];

    internal static bool IsRequest(MethodInfo? method)
    {
        if (method?.DeclaringType is not { IsInterface: true } type)
        {
            return false;
        }

        if (FrameworkInterfaces.Contains(type))
        {
            return false;
        }

        return typeof(INeuron).IsAssignableFrom(type);
    }
}
