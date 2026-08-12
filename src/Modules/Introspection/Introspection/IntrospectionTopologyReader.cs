using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Introspection;

internal sealed class IntrospectionTopologyReader(
    IGrainFactory grainFactory,
    IServiceProvider services,
    TimeProvider timeProvider,
    OwnerId owner)
{
    private const string ModulesConfigurationSection = "DigitalBrain:Modules";
    private readonly OwnerNeuronInventory _inventory = new(grainFactory, owner);

    internal async Task<TopologyRead> ReadAsync(
        CommandId commandId,
        CancellationToken cancellationToken)
    {
        var ownerStatistics = await _inventory.ReadAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        var connections = await grainFactory
            .GetGrain<ISynapseGraph>(PrincipalGraph.Resolve(owner).ToGrainId())
            .Connections()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        var placements = ownerStatistics
            .Select(static neuron => neuron.Silo)
            .Distinct()
            .OrderBy(static address => address.ToString(), StringComparer.Ordinal)
            .Select(static (address, index) => (Address: address, Label: $"cluster-{index + 1}"))
            .ToDictionary(static placement => placement.Address, static placement => placement.Label);

        return new TopologyRead(
            commandId,
            ComposedModuleIds(),
            [
                .. ownerStatistics
                    .Select(neuron => new TopologyNeuron(
                        $"{neuron.Type}:{neuron.GrainKey}",
                        neuron.Type,
                        neuron.GrainKey,
                        placements[neuron.Silo]))
                    .OrderBy(static neuron => neuron.GrainType, StringComparer.Ordinal)
                    .ThenBy(static neuron => neuron.Identity, StringComparer.Ordinal),
            ],
            timeProvider.GetUtcNow(),
            [
                .. connections
                    .Select(static connection => new TopologyConnection(
                        connection.ConnectionId,
                        connection.Source.ToString(),
                        connection.SynapseAlias,
                        connection.Target.ToString(),
                        connection.Transform,
                        connection.ExpiresAt))
                    .OrderBy(static connection => connection.Source, StringComparer.Ordinal)
                    .ThenBy(static connection => connection.SynapseAlias, StringComparer.Ordinal),
            ],
            [
                .. (services.GetService<BroadcastTopology>()?.Routes ?? [])
                    .Select(static route => new TopologyBroadcastRoute(
                        route.SynapseAlias,
                        route.HandlerGrainType)),
            ]);
    }

    private IReadOnlyList<string> ComposedModuleIds()
    {
        if (services.GetService<ActiveCapabilityCatalog>() is { Modules.Count: > 0 } catalog)
        {
            return
            [
                .. catalog.Modules
                    .Select(static module => module.ModuleId.Value)
                    .OrderBy(static id => id, StringComparer.Ordinal),
            ];
        }

        if (services.GetService<IConfiguration>() is not { } configuration)
        {
            return [];
        }

        return
        [
            .. configuration
                .GetSection(ModulesConfigurationSection)
                .GetChildren()
                .Select(static section => section.Value)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!)
                .OrderBy(static id => id, StringComparer.Ordinal),
        ];
    }
}
