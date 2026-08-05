using System.Reflection;

namespace DigitalBrain;

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

        return declared == typeof(Neuron.ITransport)
            || declared == typeof(Neuron.IDrainEntry)
            || declared == typeof(Neuron.ISessionEntry)
            || declared.Namespace?.StartsWith("Orleans", StringComparison.Ordinal) is true;
    }
}
