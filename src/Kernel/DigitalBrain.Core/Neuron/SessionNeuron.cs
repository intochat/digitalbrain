using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

internal sealed class SessionNeuron : Neuron, ISessionNeuron
{
    public Task<SynapseDelivery> Fire(NeuronId receiver, Synapse synapse)
    {
        if (receiver.Owner != Id.Owner)
        {
            throw new NeuronAuthorizationException(
                $"An owner '{Id.Owner}' session cannot fire at '{receiver}', which belongs to owner '{receiver.Owner}'.");
        }

        return SendAsync(receiver, synapse);
    }

    public Task Emit(Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        return base.EmitAsync(synapse);
    }

    public Task<JournalRead> ReadNeuronJournal(NeuronId subject, JournalKind kind, long afterSequence)
        => subject == Id
            ? ReadJournal(kind, afterSequence)
            : GrainFactory.GetGrain<INeuron>(subject.ToGrainId()).ReadJournal(kind, afterSequence);

    public Task WatchNeuron(NeuronId subject, JournalKind kind, long afterSequence, IJournalObserver observer)
        => subject == Id
            ? Watch(kind, afterSequence, observer)
            : GrainFactory.GetGrain<INeuron>(subject.ToGrainId()).Watch(kind, afterSequence, observer);

    public Task UnwatchNeuron(NeuronId subject, IJournalObserver observer)
        => subject == Id
            ? Unwatch(observer)
            : GrainFactory.GetGrain<INeuron>(subject.ToGrainId()).Unwatch(observer);
}
