namespace DigitalBrain.Contracts;

public interface INeuronObserver : IGrainObserver
{
    Task Hear(Signal signal);
}
