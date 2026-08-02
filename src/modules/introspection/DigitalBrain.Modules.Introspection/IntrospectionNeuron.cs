using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.Introspection;

[GrainType("introspection")]
internal sealed partial class IntrospectionNeuron :
    Neuron,
    IIntrospection,
    IHandle<TallyJournalRequest>,
    IHandle<ReadJournalRequest>,
    IHandle<ReadTopologyRequest>,
    IEmit<JournalTallied>,
    IEmit<JournalPageRead>,
    IEmit<TopologyRead>
{
    public async Task HandleAsync(TallyJournalRequest synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var subject = SubjectOf(synapse.NeuronType, synapse.NeuronName);
        if (RefusalFor(subject) is { } refusal)
        {
            await ReplyAsync(
                JournalTallied.Refused(synapse.CommandId, subject, synapse.Direction, refusal),
                cancellationToken);
            return;
        }

        var (read, unavailable) = await TryReadAsync(subject, synapse.Kind, BeyondJournalEnd, cancellationToken);
        if (read?.ResetSnapshot is not { } snapshot)
        {
            await ReplyAsync(
                JournalTallied.Refused(
                    synapse.CommandId,
                    subject,
                    synapse.Direction,
                    unavailable ?? $"Neuron '{subject}' returned no journal snapshot to tally."),
                cancellationToken);
            return;
        }

        await ReplyAsync(
            new JournalTallied(
                synapse.CommandId,
                subject,
                synapse.Direction,
                snapshot.TotalRecorded,
                snapshot.LastSequence,
                [.. snapshot.Tallies]),
            cancellationToken);
    }

    public async Task HandleAsync(ReadJournalRequest synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var subject = SubjectOf(synapse.NeuronType, synapse.NeuronName);
        if (RefusalFor(subject) is { } refusal)
        {
            await ReplyAsync(
                JournalPageRead.Refused(synapse.CommandId, subject, synapse.Direction, refusal),
                cancellationToken);
            return;
        }

        var (read, unavailable) = await TryReadAsync(
            subject,
            synapse.Kind,
            synapse.AfterSequence,
            cancellationToken);
        if (read is null)
        {
            await ReplyAsync(
                JournalPageRead.Refused(synapse.CommandId, subject, synapse.Direction, unavailable!),
                cancellationToken);
            return;
        }

        await ReplyAsync(
            new JournalPageRead(
                synapse.CommandId,
                subject,
                synapse.Direction,
                read.ResumeSequence,
                read.ResetSnapshot is not null,
                [
                    .. read.Delta
                        .Take(synapse.MaxEntries)
                        .Select(delivery => new JournaledFact(
                            delivery.Sequence,
                            delivery.Synapse.GetType().Name,
                            delivery.Caller.ToString(),
                            delivery.CorrelationId.ToString(),
                            delivery.Timestamp)),
                ]),
            cancellationToken);
    }

    public async Task HandleAsync(ReadTopologyRequest synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        await ReplyAsync(await ReadTopologyAsync(synapse.CommandId, cancellationToken), cancellationToken);
    }

    private NeuronId SubjectOf(string neuronType, string neuronName)
        => new(neuronType, Id.Owner, neuronName);
}
