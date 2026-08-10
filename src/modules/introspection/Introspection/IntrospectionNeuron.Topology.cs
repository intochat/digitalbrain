using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Introspection;

internal sealed partial class IntrospectionNeuron
{
    private const string ModulesConfigurationSection = "DigitalBrain:Modules";
    private const char IdentityPartSeparator = '/';

    private async Task<ActivatedNeuron[]> ActivatedOwnerNeuronsAsync(CancellationToken cancellationToken)
    {
        var statistics = await GrainFactory
            .GetGrain<IManagementGrain>(0)
            .GetDetailedGrainStatistics()
            .WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var ownerPrefix = $"{Id.Owner.Value}{IdentityPartSeparator}";

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

    private async Task<TopologyRead> ReadTopologyAsync(CommandId commandId, CancellationToken cancellationToken)
    {
        var ownerStatistics = await ActivatedOwnerNeuronsAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        var connections = await GrainFactory
            .GetGrain<ISynapseGraph>(ISynapseGraph.ForOwner(Id.Owner).ToGrainId())
            .Connections()
            .WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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
            TimeProvider.GetUtcNow(),
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
            ]);
    }

    private sealed record ActivatedNeuron(string Type, string GrainKey, SiloAddress Silo);

    private IReadOnlyList<string> ComposedModuleIds()
    {
        if (ServiceProvider.GetService<ActiveCapabilityCatalog>() is { Modules.Count: > 0 } catalog)
        {
            return
            [
                .. catalog.Modules
                    .Select(static module => module.ModuleId.Value)
                    .OrderBy(static id => id, StringComparer.Ordinal),
            ];
        }

        if (ServiceProvider.GetService<IConfiguration>() is not { } configuration)
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
