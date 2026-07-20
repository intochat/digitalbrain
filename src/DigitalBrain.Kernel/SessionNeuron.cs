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

    public new Task EmitAsync(Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        return base.EmitAsync(synapse);
    }

    public Task<JournalRead> ReadNeuronJournalAsync(NeuronId subject, JournalKind kind, long afterSequence)
        => subject == Id
            ? ReadJournalAsync(kind, afterSequence)
            : GrainFactory.GetGrain<INeuron>(subject.ToGrainId()).ReadJournalAsync(kind, afterSequence);

    public Task WatchNeuronAsync(NeuronId subject, JournalKind kind, long afterSequence, IJournalObserver observer)
        => subject == Id
            ? WatchAsync(kind, afterSequence, observer)
            : GrainFactory.GetGrain<INeuron>(subject.ToGrainId()).WatchAsync(kind, afterSequence, observer);

    public Task UnwatchNeuronAsync(NeuronId subject, IJournalObserver observer)
        => subject == Id
            ? UnwatchAsync(observer)
            : GrainFactory.GetGrain<INeuron>(subject.ToGrainId()).UnwatchAsync(observer);
}
