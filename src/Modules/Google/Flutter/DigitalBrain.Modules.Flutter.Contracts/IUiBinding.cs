using DigitalBrain;
using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter;

[Alias("ui.event-handler")]
public interface IUiEventHandler : IGrainWithStringKey
{
    [OneWay] Task HandleUiEvent(Signal signal);
}

[Alias("ui.binding"), Orleans.Metadata.DefaultGrainType("ui.binding")]
public interface IUiBinding : INeuron
{
    Task Bind(IUiEventHandler handler);
    Task Dispatch(Signal signal);
}
