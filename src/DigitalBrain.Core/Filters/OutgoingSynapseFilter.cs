namespace DigitalBrain;

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
