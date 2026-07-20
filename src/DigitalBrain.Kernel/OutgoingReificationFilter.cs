using System.Reflection;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace DigitalBrain.Kernel;

internal sealed class OutgoingReificationFilter(IServiceProvider services) : IOutgoingGrainCallFilter
{
    private static readonly HashSet<Type> FrameworkInterfaces =
    [
        typeof(INeuron),
        typeof(ISessionNeuron),
        typeof(ISubscriptionRegistry),
        typeof(IJournalObserver),
    ];

    public async Task Invoke(IOutgoingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        await context.Invoke();

        if (!IsCapabilityRequest(context.InterfaceMethod))
        {
            return;
        }

        var caller = services.GetService<IGrainContextAccessor>()?.GrainContext?.GrainInstance as Neuron;

        if (caller is null)
        {
            return;
        }

        var interfaceName = context.InterfaceMethod!.DeclaringType!.FullName!;
        var methodName = context.InterfaceMethod.Name;
        var target = context.TargetId.ToString();

        await caller.ReifyCapabilityCallAsync(interfaceName, methodName, target);
    }

    private static bool IsCapabilityRequest(MethodInfo? method)
    {
        if (method?.DeclaringType is not { IsInterface: true } type)
        {
            return false;
        }

        if (FrameworkInterfaces.Contains(type))
        {
            return false;
        }

        var ns = type.Namespace ?? string.Empty;

        if (ns.StartsWith("Orleans", StringComparison.Ordinal)
            || ns.StartsWith("DigitalBrain.Kernel", StringComparison.Ordinal)
            || ns.StartsWith("DigitalBrain.Testing", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }
}
