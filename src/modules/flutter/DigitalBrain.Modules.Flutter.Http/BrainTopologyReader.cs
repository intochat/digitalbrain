using DigitalBrain.Client;

namespace DigitalBrain.UI;

internal sealed class BrainTopologyReader(
    IDigitalBrain brain,
    IGrainFactory grains,
    IConfiguration configuration)
{
    public async Task<BrainTopologySnapshot> ReadAsync(
        CancellationToken cancellationToken)
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
        var neurons = statistics
            .Where(statistic => statistic.GrainId.Key.ToString()!
                .StartsWith(ownerPrefix, StringComparison.Ordinal))
            .Select(static statistic =>
            {
                var type = statistic.GrainId.Type.ToString()!;
                var key = statistic.GrainId.Key.ToString()!;
                return new BrainNeuron(
                    $"{type}:{key}",
                    type,
                    key,
                    statistic.SiloAddress.ToString());
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
