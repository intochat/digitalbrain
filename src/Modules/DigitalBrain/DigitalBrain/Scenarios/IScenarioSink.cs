using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

public interface IScenarioSink
{
    Task<int> RouteAsync(SignalDelivery delivery, CancellationToken cancellationToken);
}

internal sealed class NoScenarioSink : IScenarioSink
{
    public Task<int> RouteAsync(SignalDelivery delivery, CancellationToken cancellationToken) => Task.FromResult(0);
}
