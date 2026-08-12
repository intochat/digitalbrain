using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

// R8 v2: durable refusals log. Journaling IS delivery; no IHandle catalog entry.
[GrainType(IInbox.GrainTypeName)]
public sealed class InboxNeuron : Neuron, IInbox
{
    protected override Task OnUnboundSynapseAsync(
        Synapse synapse,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (synapse is not (RouteOutcome or Unrouted))
        {
            throw new NeuronAuthorizationException(
                $"Inbox '{Id}' only accepts RouteOutcome/Unrouted facts; refused '{synapse.GetType().Name}'.");
        }

        return Task.CompletedTask;
    }
}
