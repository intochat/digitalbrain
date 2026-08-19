using DigitalBrain.Client;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Kernel;

internal static class BrainTopologyHttpMaps
{
    internal static readonly TimeSpan TopologyReplyBound = TimeSpan.FromSeconds(90);

    public static IEndpointRouteBuilder MapBrainTopology(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            HttpSurfacePaths.BrainTopologyPath,
            static async Task<IResult> (
                IDigitalBrain brain,
                IGrainFactory grains,
                ActiveCapabilityCatalog catalog,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(brain);
                ArgumentNullException.ThrowIfNull(grains);
                ArgumentNullException.ThrowIfNull(catalog);
                cancellationToken.ThrowIfCancellationRequested();

                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TopologyReplyBound);

                try
                {
                    return Results.Ok(await ReadSnapshotAsync(brain, grains, catalog, deadline.Token));
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return Results.Problem(
                        "The brain did not report topology within "
                        + $"{TopologyReplyBound.TotalSeconds} seconds.",
                        statusCode: StatusCodes.Status504GatewayTimeout);
                }
            });

        return endpoints;
    }

    private static async Task<BrainTopologySnapshot> ReadSnapshotAsync(
        IDigitalBrain brain,
        IGrainFactory grains,
        ActiveCapabilityCatalog catalog,
        CancellationToken cancellationToken)
    {
        var ownerPrefix = $"{brain.Owner.Value}/";
        var statistics = await grains
            .GetGrain<IManagementGrain>(0)
            .GetDetailedGrainStatistics()
            .WaitAsync(cancellationToken);

        List<(DetailedGrainStatistic Statistic, string Identity)> ownedGrains = [];
        foreach (var statistic in statistics)
        {
            if (statistic.GrainId.Key.ToString() is { } key
                && key.StartsWith(ownerPrefix, StringComparison.Ordinal))
            {
                ownedGrains.Add((statistic, key));
            }
        }

        var placements = ownedGrains
            .Select(static owned => owned.Statistic.SiloAddress)
            .Distinct()
            .OrderBy(static address => address.ToString(), StringComparer.Ordinal)
            .Select(static (address, index) => (Address: address, Label: $"cluster-{index + 1}"))
            .ToDictionary(static placement => placement.Address, static placement => placement.Label);

        var registry = brain.GetEntity<IBrain>(DigitalBrainNames.DefaultBrain);
        var state = await registry.Read().WaitAsync(cancellationToken);
        var connections = state?.Connections ?? [];

        return new BrainTopologySnapshot(
            [
                .. catalog.Modules
                    .Select(static module => new BrainModule(module.ModuleId.Value))
                    .OrderBy(static module => module.Id, StringComparer.Ordinal),
            ],
            [
                .. ownedGrains
                    .Select(owned => new BrainNeuron(
                        $"{owned.Statistic.GrainId.Type}:{owned.Identity}",
                        owned.Statistic.GrainId.Type.ToString()!,
                        owned.Identity,
                        placements[owned.Statistic.SiloAddress]))
                    .OrderBy(static neuron => neuron.GrainType, StringComparer.Ordinal)
                    .ThenBy(static neuron => neuron.Identity, StringComparer.Ordinal),
            ],
            DateTimeOffset.UtcNow,
            [
                .. connections
                    .Select(static connection => new BrainConnection(
                        ConnectionIdentity.Of(
                            connection.From.ToString(),
                            connection.Role,
                            connection.To.ToString()),
                        connection.From.ToString(),
                        connection.Role,
                        connection.To.ToString()))
                    .OrderBy(static connection => connection.Source, StringComparer.Ordinal)
                    .ThenBy(static connection => connection.SynapseAlias, StringComparer.Ordinal),
            ],
            // Broadcast fan-out is Orleans BroadcastChannel; there is no discoverable
            // per-alias route catalog to report, so the snapshot's field stays empty.
            []);
    }
}
