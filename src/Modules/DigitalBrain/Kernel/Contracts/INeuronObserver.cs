namespace DigitalBrain.Contracts;

public interface INeuronObserver : IGrainObserver
{
    Task OnSignalAsync(Signal signal);
}