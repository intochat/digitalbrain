using DigitalBrain.Abstractions.Signals;
namespace DigitalBrain.Abstractions.Neurons;

public interface IHandle<in TSignal>
    where TSignal : Signal
{
    Task HandleAsync(TSignal signal, CancellationToken cancellationToken);
}
