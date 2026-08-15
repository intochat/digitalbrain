using DigitalBrain.Abstractions;

namespace DigitalBrain.Introspection;

internal sealed class OwnerNeuronInventory(IGrainFactory grainFactory, OwnerId owner)
{
    private const char IdentityPartSeparator = '/';

    internal async Task<ActivatedNeuron[]> ReadAsync(CancellationToken cancellationToken)
    {
        var statistics = await grainFactory
            .GetGrain<IManagementGrain>(0)
            .GetDetailedGrainStatistics()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var ownerPrefix = $"{owner.Value}{IdentityPartSeparator}";

        return
        [
            .. statistics
                .Where(statistic => statistic.GrainId.Key.ToString()!
                    .StartsWith(ownerPrefix, StringComparison.Ordinal))
                .Select(static statistic => new ActivatedNeuron(
                    statistic.GrainId.Type.ToString()!,
                    statistic.GrainId.Key.ToString()!,
                    statistic.SiloAddress)),
        ];
    }
}

internal sealed record ActivatedNeuron(string Type, string GrainKey, SiloAddress Silo);
