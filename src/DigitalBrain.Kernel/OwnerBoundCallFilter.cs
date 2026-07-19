using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

internal sealed class OwnerBoundCallFilter : IIncomingGrainCallFilter
{
    public Task Invoke(IIncomingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (OwnerOf(context.SourceId) is { } caller)
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
