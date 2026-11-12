using DigitalBrain.Kernel;
using DigitalBrain.Units;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Synapses;

internal sealed class LiveBindingsClusterListener(
    LiveBindings live,
    IClusterListeners listeners) : ILifecycleParticipant<ISiloLifecycle>
{
    private IAsyncDisposable? _sub1;
    private IAsyncDisposable? _sub2;

    public void Participate(ISiloLifecycle lifecycle)
    {
        lifecycle.Subscribe(nameof(LiveBindingsClusterListener), ServiceLifecycleStage.Active,
            async _ =>
            {
                _sub1 = await listeners.Listen<Deployed>("live.bindings", (pub, s, _) =>
                {
                    live.Set(pub.Brain, new BuildRef(s.SourceKey, s.Hash));
                    return Task.CompletedTask;
                }).ConfigureAwait(false);

                _sub2 = await listeners.Listen<Undeployed>("live.bindings", (pub, s, _) =>
                {
                    live.Retire(pub.Brain, s.SourceKey);
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            },
            async _ =>
            {
                if (_sub1 is not null) { await _sub1.DisposeAsync().ConfigureAwait(false); }
                if (_sub2 is not null) { await _sub2.DisposeAsync().ConfigureAwait(false); }
            });
    }
}
