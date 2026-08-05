using System.Reflection;

namespace DigitalBrain;

// The receiving edge of every wire call into a neuron (§5): admit ONLY the Core transport
// surface — anything else is a second wire into the grain and is refused loudly. For the
// two delivery methods the filter consumes the envelope from RequestContext and hands it
// to the receiver, whose transport method opens the turn with it; a delivery without an
// envelope is a kernel bug, never a tolerable degradation. Non-neuron grains (the outbox
// wakeup) pass through untouched.
internal sealed class IncomingSynapseFilter : IIncomingGrainCallFilter
{
    public Task Invoke(IIncomingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Grain is not Neuron receiver)
        {
            return context.Invoke();
        }

        var method = context.InterfaceMethod;
        if (!IsCoreSurface(method))
        {
            throw new InvalidOperationException(
                $"'{method?.DeclaringType?.Name}.{method?.Name}' is not the Core transport surface; "
                + $"the only wire into {receiver.Id} is Core's transport — deliveries, reads and the "
                + "drain wakeup (the §5 whitelist).");
        }

        if (method is not null && Neuron.IsDelivery(method))
        {
            var envelope = SynapseHeaders.Consume() ?? throw new InvalidOperationException(
                $"A delivery reached {receiver.Id} without its envelope headers; Core writes them "
                + "before every wire call — a delivery without an envelope is a kernel bug.");
            receiver.AcceptEnvelope(envelope);
        }

        return context.Invoke();
    }

    private static bool IsCoreSurface(MethodInfo? method)
    {
        if (method?.DeclaringType is not { } declared)
        {
            return false;
        }

        // Orleans' own runtime interfaces (grain extensions such as request cancellation)
        // are infrastructure under the whitelist, not a module wire; module-declared grain
        // interfaces are already refused at activation (NeuronConcurrency), so this filter
        // is the wire-side backstop of the same rule.
        return declared == typeof(Neuron.ITransport)
            || declared == typeof(Neuron.IDrainEntry)
            || declared == typeof(Neuron.ISessionEntry)
            || declared.Namespace?.StartsWith("Orleans", StringComparison.Ordinal) is true;
    }
}
