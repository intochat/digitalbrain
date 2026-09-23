using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Discovery;

[GrainType("capability-catalog")]
internal sealed class CapabilityCatalogNeuron(CapabilityCatalog catalog, TimeProvider time) : Neuron, ICapabilityCatalog
{
    private const int MaxTake = 25;
    private const string UnmetIntentBoardKey = "unmet-intents";

    public async Task<CapabilitySearchResult> Search(string query, string workspaceId, int take)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var clamped = Math.Clamp(take, 1, MaxTake);
        var result = await catalog.SearchAsync(query, clamped, CancellationToken.None).ConfigureAwait(true);
        if (result.Hits.Count == 0)
        {
            await RecordUnmetIntentAsync(workspaceId, query).ConfigureAwait(true);
        }

        return result;
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
