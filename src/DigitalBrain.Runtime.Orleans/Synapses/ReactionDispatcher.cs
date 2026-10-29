using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>
/// Maps a signal to the <c>[On]</c> method of a neuron class: the most specific method whose first parameter accepts the signal,
/// optionally followed by a <see cref="CancellationToken"/> (same rule as TestOS). No method means the signal is not reacted to.
/// </summary>
internal static class ReactionDispatcher
{
    private static readonly ConcurrentDictionary<(Type Class, Type Signal), MethodInfo?> Reactions = new();

    /// <summary>Runs the reaction of <paramref name="neuron"/> to <paramref name="signal"/>; returns false when it has none.</summary>
    public static async Task<bool> React(Neuron neuron, Signal signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(neuron);
        ArgumentNullException.ThrowIfNull(signal);
        if (Find(neuron.GetType(), signal.GetType()) is not { } method) { return false; }
        var arguments = method.GetParameters().Length == 2 ? new object?[] { signal, cancellationToken } : [signal];
        object? returned;
        try { returned = method.Invoke(neuron, arguments); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
        if (returned is Task task) { await task.ConfigureAwait(true); }
        else if (returned is ValueTask valueTask) { await valueTask.ConfigureAwait(true); }
        return true;
    }

    /// <summary>The reaction method of <paramref name="neuronClass"/> for <paramref name="signal"/>, or null.</summary>
    public static MethodInfo? Find(Type neuronClass, Type signal) => Reactions.GetOrAdd((neuronClass, signal), static key =>
    {
        var candidates = new List<MethodInfo>();
        for (var type = key.Class; type is not null && type != typeof(object); type = type.BaseType)
        {
            candidates.AddRange(type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.IsDefined(typeof(OnAttribute), false) && method.GetParameters() is { Length: 1 or 2 } parameters
                    && parameters[0].ParameterType.IsAssignableFrom(key.Signal)
                    && (parameters.Length == 1 || parameters[1].ParameterType == typeof(CancellationToken))));
        }
        return candidates.OrderByDescending(method => Depth(method.GetParameters()[0].ParameterType)).FirstOrDefault();
    });

    private static int Depth(Type type)
    {
        var depth = 0;
        for (var current = type; current is not null; current = current.BaseType) { depth++; }
        return depth;
    }
}
