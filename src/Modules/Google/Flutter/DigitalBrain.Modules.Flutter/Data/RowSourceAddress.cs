using DigitalBrain.Contracts.Data;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Flutter;

internal static class RowSourceAddress
{
    public static IRowSource Open(IGrainFactory grains, string neuronId)
    {
        ArgumentNullException.ThrowIfNull(grains);
        if (!GrainId.TryParse(neuronId, out var grainId))
        {
            throw new ArgumentException("A row source is addressed by its neuron id.", nameof(neuronId));
        }

        return grains.GetGrain<IRowSource>(grainId);
    }
}
