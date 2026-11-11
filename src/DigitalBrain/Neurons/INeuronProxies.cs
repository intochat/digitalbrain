namespace DigitalBrain.Neurons;

/// <summary>Typed proxies to neurons.</summary>
public interface INeuronProxies
{
    /// <summary>A typed proxy to the neuron with this id.</summary>
    T Get<T>(NeuronId id) where T : class;
}
