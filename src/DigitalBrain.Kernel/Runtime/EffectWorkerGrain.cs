using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using Orleans;

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.v2.effect-worker")]
public sealed class EffectWorkerGrain(IGrainFactory grainFactory, IServiceProvider services) : Grain, IEffectWorkerGrain
{
    public Task<EffectTransitionRecord> ExecuteAsync(string aggregateId, string effectId, string leaseOwner, TimeSpan leaseDuration)
    {
        var store = new OrleansAggregateStore(grainFactory);
        var handlers = services.GetServices<IEffectHandler>();
        var verifiers = services.GetServices<IEffectVerifier>();
        return new EffectCoordinator(store, handlers, verifiers: verifiers)
            .ExecuteOnceAsync(aggregateId, effectId, leaseOwner, leaseDuration);
    }
}
