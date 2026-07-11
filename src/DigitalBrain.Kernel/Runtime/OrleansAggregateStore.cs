using DigitalBrain.Core.Runtime;
using DigitalBrain.Core.V2;
using DigitalBrain.Kernel.Runtime;
using Orleans;

namespace DigitalBrain.Kernel;

public sealed class OrleansAggregateStore(IGrainFactory grainFactory) : IAggregateStore
{
    private IAggregateGrain Grain(string aggregateId) => grainFactory.GetGrain<IAggregateGrain>(aggregateId);
    public Task<V2AggregateSnapshot> ReadAsync(string aggregateId, CancellationToken cancellationToken = default) => Grain(aggregateId).ReadAsync();
    public Task<V2CommitResult> CommitAsync(string aggregateId, V2CommitRequest request, CancellationToken cancellationToken = default) => Grain(aggregateId).CommitAsync(request);
    public Task AppendEffectTransitionAsync(string aggregateId, EffectTransitionRecord transition, CancellationToken cancellationToken = default) => Grain(aggregateId).AppendEffectTransitionAsync(transition);
    public Task<bool> TryAppendEffectTransitionAsync(string aggregateId, string effectId, string? expectedTransitionId, EffectTransitionRecord transition, CancellationToken cancellationToken = default)
        => Grain(aggregateId).TryAppendEffectTransitionAsync(effectId, expectedTransitionId, transition);
}
