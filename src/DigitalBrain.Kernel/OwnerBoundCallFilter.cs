using System.Reflection;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

internal sealed class OwnerBoundCallFilter : IIncomingGrainCallFilter
{
    public Task Invoke(IIncomingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (OwnerOf(context.SourceId) is not { } caller)
        {
            if (context.Grain is SubscriptionRegistry unattributedRegistry)
            {
                throw new NeuronAuthorizationException(
                    $"The subscription registry of owner '{unattributedRegistry.Owner}' cannot be reached by an unattributed caller.");
            }

            if (context.Grain is Neuron unattributedTarget && !IsClientEntryPoint(context.InterfaceMethod))
            {
                throw new NeuronAuthorizationException(
                    $"'{context.InterfaceMethod?.Name}' is not a client entry point, so an unattributed caller cannot be authorized to reach '{unattributedTarget.Id}'. Reach a neuron through a session of the owner you are acting as.");
            }
        }
        else
        {
            if (context.Grain is Neuron target && caller != target.Id.Owner)
            {
                throw new NeuronAuthorizationException(
                    $"Neuron '{target.Id}' belongs to owner '{target.Id.Owner}' and cannot be reached by owner '{caller}'.");
            }

            if (context.Grain is SubscriptionRegistry registry && caller != registry.Owner)
            {
                throw new NeuronAuthorizationException(
                    $"The subscription registry of owner '{registry.Owner}' cannot be reached by owner '{caller}'.");
            }
        }

        return context.Invoke();
    }

    private static bool IsClientEntryPoint(MethodInfo? method)
        => method?.DeclaringType?.GetCustomAttribute<ClientEntryPointAttribute>() is not null;

    private static OwnerId? OwnerOf(GrainId? source)
    {
        if (source?.Key.ToString() is not { } key)
        {
            return null;
        }

        var separator = key.IndexOf(IdentityPartSeparator, StringComparison.Ordinal);

        return separator <= 0 ? null : new OwnerId(key[..separator]);
    }

    private const char IdentityPartSeparator = '/';
}
