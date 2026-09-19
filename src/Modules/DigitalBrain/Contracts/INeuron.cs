namespace DigitalBrain.Contracts;
public interface INeuron : IGrainWithStringKey
{
    Task<Guid> Watch(INeuronObserver observer);
    Task Unwatch(INeuronObserver observer);
}
