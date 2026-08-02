using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

internal static class GrainOwnership
{
    internal static OwnerId RequireOwner(GrainId grain)
    {
        var key = grain.Key.ToString();
        var separator = key.IndexOf('/', StringComparison.Ordinal);

        if (separator <= 0)
        {
            throw new NeuronAuthorizationException(
                $"Delegated runner '{grain}' has no owner-bound grain key.");
        }

        return new OwnerId(key[..separator]);
    }
}
