using Orleans.Concurrency;

namespace DigitalBrain;

internal static class NeuronConcurrency
{
    private static readonly HashSet<Type> CoreInterfaces =
    [
        typeof(Neuron.ITransport), typeof(Neuron.IDrainEntry), typeof(IIngress),
        typeof(IGrainWithStringKey), typeof(IGrain),
    ];

    internal static void RequireSerializedTurns(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.IsDefined(typeof(ReentrantAttribute), inherit: true)
            || type.IsDefined(typeof(MayInterleaveAttribute), inherit: true)
            || type.IsDefined(typeof(StatelessWorkerAttribute), inherit: true)
            || typeof(IRemindable).IsAssignableFrom(type))
        {
            throw new InvalidOperationException($"{type.Name} cannot opt out of serialized neuron turns.");
        }

        foreach (var contract in type.GetInterfaces())
        {
            if (typeof(IAddressable).IsAssignableFrom(contract)
                && contract != typeof(IAddressable)
                && !CoreInterfaces.Contains(contract))
            {
                throw new InvalidOperationException($"{type.Name} exposes a second grain wire.");
            }
        }
    }
}
