using System.Reflection;
using DigitalBrain.Abstractions;
using Orleans.Concurrency;

using DigitalBrain.Abstractions.Neurons;
namespace DigitalBrain.Core;

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
            .Where(method => !IsKernelFreeRead(method))
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

    // Read methods declared by INeuronQuery are the kernel's free observation plane: no journal
    // entry or correlation, and safe to interleave because they only observe durable state.
    // Watch and Unwatch carry no interleaving attribute and therefore remain serialized.
    private static bool IsKernelFreeRead(MethodInfo method)
        => method.DeclaringType == typeof(INeuronQuery);

    private static void Refuse(Type neuronType, string attribute)
        => throw new InvalidOperationException(
            $"{neuronType.Name} uses {attribute} outside {nameof(INeuronQuery)}, but neurons require serialized turns to "
            + "preserve journal order and delivery lineage.");
}
