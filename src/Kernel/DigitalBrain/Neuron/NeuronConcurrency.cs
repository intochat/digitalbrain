using System.Reflection;
using Orleans.Concurrency;

using DigitalBrain.Abstractions.Neurons;
namespace DigitalBrain.Core;

internal static class NeuronConcurrency
{
    private static readonly HashSet<string> KernelInterleavedMethods = new(StringComparer.Ordinal)
    {
        nameof(INeuron.Deliver),
        nameof(INeuron.CancelReaction),
        nameof(INeuron.ReadJournal),
        nameof(INeuron.ReadCommands),
        nameof(INeuron.ReadPendingCount),
    };

    static NeuronConcurrency()
    {
        if (KernelInterleavedMethods.Count != 5)
        {
            throw new InvalidOperationException("Exactly five kernel operations may interleave. Revisit the specification before changing this set.");
        }
    }

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
            .Where(method => !IsKernelInterleaved(method))
            .ToArray();

        if (methods.Any(method => method.IsDefined(typeof(AlwaysInterleaveAttribute), inherit: true)))
        {
            Refuse(neuronType, nameof(AlwaysInterleaveAttribute));
        }
    }

    // AlwaysInterleave is kernel-only; module methods may use ReadOnly.
    private static bool IsKernelInterleaved(MethodInfo method)
        => method.DeclaringType == typeof(INeuron)
        && KernelInterleavedMethods.Contains(method.Name);

    private static void Refuse(Type neuronType, string attribute)
        => throw new InvalidOperationException(
            $"{neuronType.Name} uses {attribute}; only kernel methods may use AlwaysInterleave, and neurons require "
            + "serialized turns to preserve journal order and delivery lineage.");
}
