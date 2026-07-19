using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

internal sealed class SessionNeuron : Neuron, ISessionNeuron
{
    public Task FireAsync(NeuronId receiver, Synapse synapse)
    {
        if (receiver.Owner != Id.Owner)
        {
            throw new NeuronAuthorizationException(
                $"An owner '{Id.Owner}' session cannot fire at '{receiver}', which belongs to owner '{receiver.Owner}'.");
        }

        return SendAsync(receiver, synapse);
    }
}
