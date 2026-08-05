using Orleans.Concurrency;

namespace DigitalBrain;

internal static class NeuronConcurrency
{
    // Core-owned grain interfaces: the transport (and the key shapes it derives from) is
    // the only wire into a neuron. Anything else IAddressable-derived on a neuron type is
    // a second wire into the grain that bypasses the turn pipeline — refused.
    private static readonly HashSet<Type> CoreOwnedInterfaces =
    [
        typeof(Neuron.ITransport), typeof(Neuron.IDrainEntry), typeof(Neuron.ISessionEntry),
        typeof(IGrainWithStringKey), typeof(IGrain),
    ];

    internal static void RequireSerializedTurns(Type neuronType)
    {
        ArgumentNullException.ThrowIfNull(neuronType);

        if (neuronType.IsDefined(typeof(ReentrantAttribute), inherit: true))
        {
            Refuse(neuronType, nameof(ReentrantAttribute));
        }

        if (neuronType.IsDefined(typeof(MayInterleaveAttribute), inherit: true))
        {
            Refuse(neuronType, nameof(MayInterleaveAttribute));
        }

        if (neuronType.IsDefined(typeof(StatelessWorkerAttribute), inherit: true))
        {
            Refuse(neuronType, nameof(StatelessWorkerAttribute));
        }

        var methods = neuronType
            .GetMethods()
            .Concat(neuronType.GetInterfaces()
                .Where(contract => !CoreOwnedInterfaces.Contains(contract))
                .SelectMany(contract => contract.GetMethods()));
        foreach (var method in methods)
        {
            if (method.IsDefined(typeof(AlwaysInterleaveAttribute), inherit: true))
            {
                Refuse(neuronType, nameof(AlwaysInterleaveAttribute));
            }

            if (method.IsDefined(typeof(ReadOnlyAttribute), inherit: true))
            {
                Refuse(neuronType, nameof(ReadOnlyAttribute));
            }
        }

        if (typeof(IRemindable).IsAssignableFrom(neuronType))
        {
            throw new InvalidOperationException(
                $"{neuronType.Name} implements {nameof(IRemindable)}, but reminders are Core wakeup machinery; "
                + "schedule facts instead.");
        }

        foreach (var contract in neuronType.GetInterfaces())
        {
            if (typeof(IAddressable).IsAssignableFrom(contract)
                && contract != typeof(IAddressable)
                && !CoreOwnedInterfaces.Contains(contract))
            {
                throw new InvalidOperationException(
                    $"{neuronType.Name} implements grain interface {contract.Name}, but the only wire into a "
                    + "neuron is Core's transport; a second grain interface bypasses the turn pipeline.");
            }
        }
    }

    private static void Refuse(Type neuronType, string attribute)
        => throw new InvalidOperationException(
            $"{neuronType.Name} uses {attribute}, but neurons require serialized turns to preserve "
            + "journal order and delivery lineage.");
}
