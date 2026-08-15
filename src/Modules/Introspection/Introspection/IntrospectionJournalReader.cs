using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Introspection;

internal sealed class IntrospectionJournalReader(IGrainFactory grainFactory, OwnerId owner)
{
    private const long BeyondJournalEnd = long.MaxValue;

    internal static readonly TimeSpan ReadBound = DeliveryPolicy.InnerDeliveryReadBound;

    private readonly OwnerNeuronInventory _inventory = new(grainFactory, owner);

    internal async Task<JournalTallied> TallyAsync(
        TallyJournalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var subject = SubjectOf(request.NeuronType, request.NeuronName);
        if (await RefusalForAsync(subject, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext) is { } refusal)
        {
            return JournalTallied.Refused(request.CommandId, subject, request.Direction, refusal);
        }

        var (read, unanswered) = await TryReadAsync(
                subject,
                request.Kind,
                BeyondJournalEnd,
                cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        if (read?.ResetSnapshot is not { } snapshot)
        {
            return JournalTallied.Refused(
                request.CommandId,
                subject,
                request.Direction,
                unanswered ?? $"Neuron '{subject}' returned no journal snapshot to tally.");
        }

        return new JournalTallied(
            request.CommandId,
            subject,
            request.Direction,
            snapshot.TotalRecorded,
            snapshot.LastSequence,
            [.. snapshot.Tallies]);
    }

    internal async Task<JournalPageRead> ReadPageAsync(
        ReadJournalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var subject = SubjectOf(request.NeuronType, request.NeuronName);
        if (await RefusalForAsync(subject, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext) is { } refusal)
        {
            return JournalPageRead.Refused(request.CommandId, subject, request.Direction, refusal);
        }

        var (read, unanswered) = await TryReadAsync(
                subject,
                request.Kind,
                request.AfterSequence,
                cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        if (read is null)
        {
            return JournalPageRead.Refused(
                request.CommandId,
                subject,
                request.Direction,
                unanswered!);
        }

        JournaledFact[] entries =
        [
            .. read.Delta
                .Take(request.MaxEntries)
                .Select(delivery => new JournaledFact(
                    delivery.Sequence,
                    delivery.Synapse.GetType().Name,
                    delivery.Caller.ToString(),
                    delivery.CorrelationId.ToString(),
                    delivery.Timestamp)),
        ];

        var truncated = read.Delta.Count > entries.Length;

        return new JournalPageRead(
            request.CommandId,
            subject,
            request.Direction,
            truncated ? entries[^1].Sequence : read.ResumeSequence,
            read.ResetSnapshot is not null,
            entries);
    }

    private NeuronId SubjectOf(string neuronType, string neuronName)
        => new(neuronType, owner, neuronName);

    private async Task<string?> RefusalForAsync(
        NeuronId subject,
        CancellationToken cancellationToken)
    {
        var activated = await _inventory.ReadAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        if (!activated.Any(neuron =>
            string.Equals(neuron.Type, subject.Type, StringComparison.OrdinalIgnoreCase)))
        {
            return $"No neuron of type '{subject.Type}' is activated for this owner. Ask "
                + "introspection.read-topology-request for the neuron types that are running.";
        }

        if (!activated.Any(neuron =>
            string.Equals(neuron.Type, subject.Type, StringComparison.OrdinalIgnoreCase)
            && string.Equals(neuron.GrainKey, subject.GrainKey, StringComparison.Ordinal)))
        {
            return $"No neuron '{subject}' is activated. Introspection never activates a neuron in "
                + "order to look at it; ask introspection.read-topology-request for the neurons that "
                + "are running.";
        }

        return null;
    }

    private async Task<(JournalRead? Read, string? Unanswered)> TryReadAsync(
        NeuronId subject,
        JournalKind kind,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        try
        {
            var read = await grainFactory
                .GetGrain<INeuron>(subject.ToGrainId())
                .ReadJournal(kind, afterSequence)
                .WaitAsync(ReadBound, cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            return (read, null);
        }
        catch (TimeoutException)
        {
            return (null, $"Neuron '{subject}' did not answer a journal read within "
                + $"{ReadBound.TotalSeconds} seconds. A journal read interleaves the subject's "
                + "turn, so this is an unreachable neuron rather than a busy one.");
        }
    }
}
