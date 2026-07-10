using DigitalBrain.Core.V2;
using DigitalBrain.Kernel.V2;
using Orleans;

namespace DigitalBrain.Kernel;

public sealed class OrleansV2AggregateStore(IGrainFactory grainFactory) : IV2AggregateStore
{
    private IV2AggregateGrain Grain(string aggregateId) => grainFactory.GetGrain<IV2AggregateGrain>(aggregateId);
    public Task<V2AggregateSnapshot> ReadAsync(string aggregateId, CancellationToken cancellationToken = default) => Grain(aggregateId).ReadAsync();
    public Task<V2CommitResult> CommitAsync(string aggregateId, V2CommitRequest request, CancellationToken cancellationToken = default) => Grain(aggregateId).CommitAsync(request);
    public Task AppendEffectTransitionAsync(string aggregateId, EffectTransitionRecord transition, CancellationToken cancellationToken = default) => Grain(aggregateId).AppendEffectTransitionAsync(transition);
}
