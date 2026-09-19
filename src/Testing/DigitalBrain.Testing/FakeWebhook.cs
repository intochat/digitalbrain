using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Scenarios;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;

namespace DigitalBrain.Testing;

public sealed class FakeWebhook(IGrainFactory grains, string scenario)
{
    public async Task PostAsync(INeuron source, Signal signal, CancellationToken cancellationToken = default)
    {
        var delivery = SignalDelivery.Create(signal, source, 1, TimeProvider.System);
        await source.Deliver(delivery, cancellationToken).ConfigureAwait(false);
        await grains.GetGrain<IScenario>(scenario).Route(delivery, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class ScenarioSink(IGrainFactory grains, string scenario) : IScenarioSink
{
    public Task<int> RouteAsync(SignalDelivery delivery, CancellationToken cancellationToken)
        => grains.GetGrain<IScenario>(scenario).Route(delivery, cancellationToken);
}
