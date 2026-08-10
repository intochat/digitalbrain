using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Introspection;

internal sealed partial class IntrospectionNeuron
{

    private const long BeyondJournalEnd = long.MaxValue;

    internal static readonly TimeSpan JournalReadBound = DeliveryPolicy.InnerDeliveryReadBound;

    private async Task<string?> RefusalForAsync(NeuronId subject, CancellationToken cancellationToken)
    {

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
