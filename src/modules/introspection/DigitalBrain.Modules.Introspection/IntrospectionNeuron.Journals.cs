using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.Introspection;

internal sealed partial class IntrospectionNeuron
{
    // NeuronFeed answers a cursor it cannot serve with a snapshot, and the snapshot is the only
    // surface that carries the per-synapse-type tallies. Asking beyond the end always yields one.
    private const long BeyondJournalEnd = long.MaxValue;

    private const int OccupiedSubjectSeconds = 5;

    private static readonly TimeSpan OccupiedSubjectTimeout = TimeSpan.FromSeconds(OccupiedSubjectSeconds);

    private async Task<string?> RefusalForAsync(NeuronId subject, CancellationToken cancellationToken)
    {
        if (subject == Id)
        {
            return $"Neuron '{subject}' is this introspection neuron, which is occupied by this very request.";
        }

        if (string.Equals(subject.Type, ISessionNeuron.GrainTypeName, StringComparison.Ordinal))
        {
            return $"Neuron '{subject}' is the session that delivers every capability request, so it is "
                + "mid-turn while this request runs; reading it would deadlock the turn. Ask for the "
                + "neuron that produced the facts instead.";
        }

        // Resolving against activated neurons before touching GrainFactory keeps two failures out of
        // the kernel: an unknown grain type would throw out of the handler and leave the outbox
        // retrying for its whole horizon, and an unknown name would silently activate a fresh grain
        // just by being asked about.
        var activated = await ActivatedOwnerNeuronsAsync(cancellationToken);

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

    private async Task<(JournalRead? Read, string? Unavailable)> TryReadAsync(
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
                .WaitAsync(OccupiedSubjectTimeout, cancellationToken);

            return (read, null);
        }
        catch (TimeoutException)
        {
            return (null, OccupiedSubjectMessage(subject));
        }
    }

    private static string OccupiedSubjectMessage(NeuronId subject)
        => $"Neuron '{subject}' did not answer within {OccupiedSubjectSeconds} seconds. A neuron serves "
            + "one turn at a time, so a neuron taking part in the turn that asked this question cannot "
            + "report on itself until that turn ends.";
}
