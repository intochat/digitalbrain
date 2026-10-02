namespace DigitalBrain;

public interface IDigitalBrain : IAsyncDisposable
{
    T Get<T>(NeuronId neuron) where T : class, INeuron;
}
