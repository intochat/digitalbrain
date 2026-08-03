using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.Introspection;

internal sealed partial class IntrospectionNeuron
{
    // NeuronFeed answers a cursor it cannot serve with a snapshot, and the snapshot is the only
    // surface that carries the per-synapse-type tallies. Asking beyond the end always yields one.
    private const long BeyondJournalEnd = long.MaxValue;

    private async Task<string?> RefusalForAsync(NeuronId subject, CancellationToken cancellationToken)
    {
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

    private Task<JournalRead> ReadAsync(NeuronId subject, JournalKind kind, long afterSequence)
        => GrainFactory.GetGrain<INeuron>(subject.ToGrainId()).ReadJournal(kind, afterSequence);
}
