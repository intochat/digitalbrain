using DigitalBrain.Client;

namespace DigitalBrain.Flutter.Http;

internal sealed class BrainTopologyReader(IDigitalBrain brain, IGrainFactory grains, IConfiguration configuration)
{
    public async Task<BrainTopologySnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var modules = configuration
            .GetSection("DigitalBrain:Modules")
            .GetChildren()
            .Select(section => section.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => new BrainModule(value!))
            .OrderBy(static module => module.Id, StringComparer.Ordinal)
            .ToArray();

        var statistics = await grains
            .GetGrain<IManagementGrain>(0)
            .GetDetailedGrainStatistics();
        cancellationToken.ThrowIfCancellationRequested();

        var ownerPrefix = $"{brain.Owner.Value}/";
        var ownerStatistics = statistics
            .Where(statistic => statistic.GrainId.Key.ToString()!
                .StartsWith(ownerPrefix, StringComparison.Ordinal))
            .ToArray();
        var placements = ownerStatistics
            .Select(static statistic => statistic.SiloAddress)
            .Distinct()
            .OrderBy(static address => address.ToString(), StringComparer.Ordinal)
            .Select(static (address, index) => (Address: address, Label: $"cluster-{index + 1}"))
            .ToDictionary(static placement => placement.Address, static placement => placement.Label);
        var neurons = ownerStatistics
            .Select(statistic =>
            {
                var type = statistic.GrainId.Type.ToString()!;
                var key = statistic.GrainId.Key.ToString()!;
                return new BrainNeuron($"{type}:{key}", type, key, placements[statistic.SiloAddress]);
            })
            .OrderBy(static neuron => neuron.GrainType, StringComparer.Ordinal)
            .ThenBy(static neuron => neuron.Identity, StringComparer.Ordinal)
            .ToArray();

        return new BrainTopologySnapshot(
            modules,
            neurons,
            TimeProvider.System.GetUtcNow());
    }
}
