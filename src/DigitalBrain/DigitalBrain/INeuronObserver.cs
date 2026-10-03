namespace DigitalBrain;

public interface INeuronObserver : IGrainObserver
{
    Task OnSignalAsync(Signal signal);
}
