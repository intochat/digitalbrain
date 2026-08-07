using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.Introspection;

internal sealed partial class IntrospectionNeuron
{
    // NeuronFeed answers a cursor it cannot serve with a snapshot, and the snapshot is the only
    // surface that carries the per-synapse-type tallies. Asking beyond the end always yields one.
    private const long BeyondJournalEnd = long.MaxValue;

    // The read interleaves, so it no longer waits out the subject's turn - but a subject that never
    // answers must not outlive one outbox delivery attempt, or the retry of the request being served
    // starts while this handler is still waiting. INeuron.ReadJournal's own ResponseTimeout is five
    // minutes, ten times that budget, so the bound has to be applied here. The bound must also come
    // in strictly under DeliveryAttemptTimeout: TryDeliverAsync arms the outer attempt deadline
    // before this handler's turn starts, so a bound equal to it always loses that race and this
    // catch would never see a TimeoutException.
    internal static readonly TimeSpan JournalReadBound = DeliveryPolicy.InnerDeliveryReadBound;

    private async Task<string?> RefusalForAsync(NeuronId subject, CancellationToken cancellationToken)
    {
        // Resolving against activated neurons before touching GrainFactory keeps two failures out of
        // the kernel: an unknown grain type would throw out of the handler and leave the outbox
        // retrying for its whole horizon, and an unknown name would silently activate a fresh grain
        // just by being asked about.
        var activated = await ActivatedOwnerNeuronsAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        if (!activated.Any(neuron => string.Equals(neuron.Type, subject.Type, StringComparison.OrdinalIgnoreCase)))
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
            var read = await GrainFactory
                .GetGrain<INeuron>(subject.ToGrainId())
                .ReadJournal(kind, afterSequence)
                .WaitAsync(JournalReadBound, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            return (read, null);
        }
        catch (TimeoutException)
        {
            return (null, $"Neuron '{subject}' did not answer a journal read within "
                + $"{JournalReadBound.TotalSeconds} seconds. A journal read interleaves the subject's "
                + "turn, so this is an unreachable neuron rather than a busy one.");
        }
    }
}
