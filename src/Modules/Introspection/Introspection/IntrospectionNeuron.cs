using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Introspection;

[GrainType("introspection")]
public sealed partial class IntrospectionNeuron :
    Neuron,
    IIntrospection,
    IHandle<TallyJournalRequest>,
    IHandle<ReadJournalRequest>,
    IHandle<ReadTopologyRequest>
{
    public async Task HandleAsync(TallyJournalRequest synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var subject = SubjectOf(synapse.NeuronType, synapse.NeuronName);
        if (await RefusalForAsync(subject, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext) is { } refusal)
        {
            await ReplyAsync(
                JournalTallied.Refused(synapse.CommandId, subject, synapse.Direction, refusal),
                cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        var (read, unanswered) = await TryReadAsync(subject, synapse.Kind, BeyondJournalEnd, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        if (read?.ResetSnapshot is not { } snapshot)
        {
            await ReplyAsync(
                JournalTallied.Refused(
                    synapse.CommandId,
                    subject,
                    synapse.Direction,
                    unanswered ?? $"Neuron '{subject}' returned no journal snapshot to tally."),
                cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(ReadJournalRequest synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var subject = SubjectOf(synapse.NeuronType, synapse.NeuronName);
        if (await RefusalForAsync(subject, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext) is { } refusal)
        {
            await ReplyAsync(
                JournalPageRead.Refused(synapse.CommandId, subject, synapse.Direction, refusal),
                cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        var (read, unanswered) = await TryReadAsync(
            subject,
            synapse.Kind,
            synapse.AfterSequence,
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        if (read is null)
        {
            await ReplyAsync(
                JournalPageRead.Refused(synapse.CommandId, subject, synapse.Direction, unanswered!),
                cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        JournaledFact[] entries =
        [
            .. read.Delta
                .Take(synapse.MaxEntries)
                .Select(delivery => new JournaledFact(
                    delivery.Sequence,
                    delivery.Synapse.GetType().Name,
                    delivery.Caller.ToString(),
                    delivery.CorrelationId.ToString(),
                    delivery.Timestamp)),
        ];

        var truncated = read.Delta.Count > entries.Length;

        await ReplyAsync(
            new JournalPageRead(
                synapse.CommandId,
                subject,
                synapse.Direction,
                truncated ? entries[^1].Sequence : read.ResumeSequence,
                read.ResetSnapshot is not null,
                entries),
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(ReadTopologyRequest synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        await ReplyAsync(await ReadTopologyAsync(synapse.CommandId, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext), cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private NeuronId SubjectOf(string neuronType, string neuronName)
        => new(neuronType, Id.Owner, neuronName);
}
