using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Kernel;

internal static class NeuronConcurrency
{
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
            .Concat(neuronType.GetInterfaces().SelectMany(contract => contract.GetMethods()))
            .ToArray();

        if (methods.Any(method => method.IsDefined(typeof(AlwaysInterleaveAttribute), inherit: true)))
        {
            Refuse(neuronType, nameof(AlwaysInterleaveAttribute));
        }

        if (methods.Any(method => method.IsDefined(typeof(ReadOnlyAttribute), inherit: true)))
        {
            Refuse(neuronType, nameof(ReadOnlyAttribute));
        }
    }

    internal static void RequireSerializedTimer(GrainTimerCreationOptions options)
    {
        if (options.Interleave)
        {
            throw new InvalidOperationException(
                $"{nameof(GrainTimerCreationOptions)}.{nameof(GrainTimerCreationOptions.Interleave)} must be false because neurons require serialized turns.");
        }
    }

    private static void Refuse(Type neuronType, string attribute)
        => throw new InvalidOperationException(
            $"{neuronType.Name} uses {attribute}, but neurons require serialized turns to preserve journal order and delivery lineage.");
}
