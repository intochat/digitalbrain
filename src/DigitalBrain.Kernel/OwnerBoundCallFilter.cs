using Orleans;
using Orleans.Runtime;

namespace DigitalBrain;

internal sealed class OwnerBoundCallFilter : IIncomingGrainCallFilter
{
    public Task Invoke(IIncomingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Grain is Neuron target && OwnerOf(context.SourceId) is { } caller && caller != target.Id.Owner)
        {
            throw new NeuronAuthorizationException(
                $"Neuron '{target.Id}' belongs to owner '{target.Id.Owner}' and cannot be reached by owner '{caller}'.");
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
