using DigitalBrain.Core.V2;
using DigitalBrain.Kernel.V2;
using Orleans;

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.v2.effect-worker")]
public sealed class V2EffectWorkerGrain(IGrainFactory grainFactory, IServiceProvider services) : Grain, IV2EffectWorkerGrain
{
    public Task<EffectTransitionRecord> ExecuteAsync(string aggregateId, string effectId, string leaseOwner, TimeSpan leaseDuration)
    {
        var store = new OrleansV2AggregateStore(grainFactory);
        var handlers = services.GetServices<IV2EffectHandler>();
        return new V2EffectCoordinator(store, handlers).ExecuteOnceAsync(aggregateId, effectId, leaseOwner, leaseDuration);
    }
}
