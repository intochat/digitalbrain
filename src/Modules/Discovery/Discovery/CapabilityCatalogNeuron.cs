using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Contracts.Registry;
using DigitalBrain.Core.Registry;
using Orleans.Runtime;

namespace DigitalBrain.Discovery;

[GrainType("capability-catalog")]
internal sealed class CapabilityCatalogNeuron(CapabilityCatalog catalog, INeuronRegistry selectedRegistry, TimeProvider time) : Neuron, ICapabilityCatalog
{
    private const int MaxTake = 25;
    private const string UnmetIntentBoardKey = "unmet-intents";

    public async Task<CapabilitySearchResult> Search(string query, string workspaceId, int take)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var clamped = Math.Clamp(take, 1, MaxTake);
        var result = await catalog.SearchAsync(query, workspaceId, clamped, CancellationToken.None).ConfigureAwait(true);
        if (result.Hits.Count == 0)
        {
            await RecordUnmetIntentAsync(workspaceId, query).ConfigureAwait(true);
        }

        return result;
    }

    public Task<CapabilitySearchResult> SearchApps(string query, string workspaceId, int take)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        return catalog.SearchAsync(query, workspaceId, Math.Clamp(take, 1, MaxTake), CancellationToken.None, appsOnly: true).AsTask();
    }

    public async Task<NeuronCapabilityDetails?> ReadNeuron(string id)
    {
        var descriptor = await GrainFactory.GetGrain<INeuronRegistryGrain>(selectedRegistry.Version).Find(id);
        return descriptor is { AgentRoutable: true }
            ? new NeuronCapabilityDetails
            {
                Id = descriptor.Id,
                Name = descriptor.Name,
                Description = descriptor.Description,
                ContractType = descriptor.ContractType,
                ModuleId = descriptor.ModuleId,
            }
            : null;
    }

    private async Task RecordUnmetIntentAsync(string workspaceId, string query)
    {
        try
        {
            await GrainFactory.GetGrain<IUnmetIntentBoard>(UnmetIntentBoardKey)
                .Record(workspaceId ?? string.Empty, query, time.GetUtcNow())
                .ConfigureAwait(true);
        }
#pragma warning disable CA1031 // recording a miss must never block or fail a turn
        catch (Exception)
        {
        }
#pragma warning restore CA1031
    }
}
