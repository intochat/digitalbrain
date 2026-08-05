namespace DigitalBrain;

// The sending edge of every proxied call (§5): a proxied self-call deadlocks under
// serialized turns (proven) — converted here into a loud exception naming the rule; and
// for the two delivery methods the sender's staged envelope becomes RequestContext headers
// just before the wire call, so the receiver's incoming filter can consume it.
internal sealed class OutgoingSynapseFilter : IOutgoingGrainCallFilter
{
    public Task Invoke(IOutgoingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.SourceContext is { } source)
        {
            if (source.GrainId == context.TargetId)
            {
                throw new InvalidOperationException(
                    $"{source.GrainId} called itself through the proxy; a proxied self-call deadlocks "
                    + "under serialized turns (proven) — self-delivery is a direct method call on the "
                    + "activation, never GrainFactory.");
            }

            if (source.GrainInstance is Neuron sender
                && context.InterfaceMethod is { } method
                && Neuron.IsDelivery(method))
            {
                SynapseHeaders.Write(sender.TakeOutboundDelivery());
            }
        }

        return context.Invoke();
    }
}
