namespace DigitalBrain.Runtime.Neurons;

/// <summary>
/// Unified generic stateful neuron interface.
/// </summary>
public interface INeuron<TState>
{
    TState State { get; set; }
    Task OnActivatedAsync();
    Task OnDeactivatedAsync();
    Task<Synapse> OnSynapseReceivedAsync(Synapse synapse);
}
