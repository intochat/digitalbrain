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

    public Task<JournalRead> ReadNeuronJournalAsync(NeuronId subject, JournalKind kind, long afterSequence)
        => subject == Id
            ? ReadJournalAsync(kind, afterSequence)
            : GrainFactory.GetGrain<INeuron>(subject.ToGrainId()).ReadJournalAsync(kind, afterSequence);
}
