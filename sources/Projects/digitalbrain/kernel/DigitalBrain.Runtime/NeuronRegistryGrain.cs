using Orleans.Journaling;

namespace DigitalBrain.Runtime;

[GrainType("NeuronRegistryGrain")]
public sealed class NeuronRegistryGrain(
    [FromKeyedServices("neurons")] IDurableDictionary<string, DynamicNeuronSpec> store)
    : DurableGrain, INeuronRegistry
{
    public async Task StageAsync(DynamicNeuronSpec spec)
    {
        // Re-staging the same NeuronId is intentional — the Creator iterates on
        // a draft until the .feature goes green. Always overwrite.
        store[spec.Id.Value] = spec with { Status = DynamicNeuronStatus.Staged };
        await WriteStateAsync();
    }

    public async Task PromoteAsync(NeuronId id)
    {
        if (!store.TryGetValue(id.Value, out var existing))
            throw new InvalidOperationException(
                $"Cannot promote unknown neuron '{id.Value}'. Stage first.");
        store[id.Value] = existing with { Status = DynamicNeuronStatus.Promoted };
        await WriteStateAsync();
    }

    public async Task RetireAsync(NeuronId id)
    {
        if (!store.TryGetValue(id.Value, out var existing)) return;
        store[id.Value] = existing with { Status = DynamicNeuronStatus.Retired };
        await WriteStateAsync();
    }

    public Task<DynamicNeuronSpec?> GetAsync(NeuronId id)
        => Task.FromResult(store.TryGetValue(id.Value, out var spec) ? spec : null);

    public Task<IReadOnlyList<DynamicNeuronSpec>> ListAsync(DynamicNeuronStatus? status = null)
    {
        IReadOnlyList<DynamicNeuronSpec> items = status is null
            ? store.Values.ToArray()
            : store.Values.Where(s => s.Status == status.Value).ToArray();
        return Task.FromResult(items);
    }
}
