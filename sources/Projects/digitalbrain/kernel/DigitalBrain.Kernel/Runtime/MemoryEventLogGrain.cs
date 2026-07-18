using DigitalBrain.Runtime.Runtime;
using Orleans.Journaling;

namespace DigitalBrain.Kernel.Runtime;

internal sealed class MemoryEventLogGrain(
    [FromKeyedServices("entries")] IDurableList<SynapseEnvelope> entries,
    [FromKeyedServices("policy")] IDurableValue<DecayPolicy> policy)
    : DurableGrain, IMemoryEventLogGrain
{
    public async Task AppendAsync(SynapseEnvelope envelope)
    {
        entries.Add(envelope);
        await PruneDecayAsync();
        await WriteStateAsync();
    }

    public Task<IReadOnlyList<SynapseEnvelope>> QueryAsync(DateTimeOffset? since, int? limit)
    {
        var list = entries.AsEnumerable();
        if (since.HasValue)
            list = list.Where(e => e.At >= since.Value);
        if (limit.HasValue)
            list = list.TakeLast(limit.Value);
        return Task.FromResult<IReadOnlyList<SynapseEnvelope>>(list.ToList());
    }

    public async Task ConfigureDecayPolicyAsync(DecayPolicy newPolicy)
    {
        policy.Value = newPolicy;
        await PruneDecayAsync();
        await WriteStateAsync();
    }

    private Task PruneDecayAsync()
    {
        var currentPolicy = policy.Value;
        if (currentPolicy is null) return Task.CompletedTask;

        // Trim count
        if (currentPolicy.MaxCount.HasValue)
        {
            while (entries.Count > currentPolicy.MaxCount.Value)
            {
                entries.RemoveAt(0);
            }
        }
        // Trim age
        if (currentPolicy.MaxAge.HasValue)
        {
            var cutoff = DateTimeOffset.UtcNow - currentPolicy.MaxAge.Value;
            while (entries.Count > 0 && entries[0].At < cutoff)
            {
                entries.RemoveAt(0);
            }
        }
        return Task.CompletedTask;
    }
}
