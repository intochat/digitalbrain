using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using DigitalBrain.Neurons;

namespace DigitalBrain.Kernel;

/// <summary>
/// Domain rules for signal reaction routing, alias resolution, edge resolution, and deterministic reaction sorting.
/// Zero dependencies on Orleans or test frameworks.
/// </summary>
internal static class ReactionTable
{
    private static readonly ConcurrentDictionary<(Type Class, Type Signal), MethodInfo?> Reactions = new();

    /// <summary>Finds the most specific [On] reaction method on <paramref name="neuronClass"/> for <paramref name="signal"/>.</summary>
    public static MethodInfo? Find(Type neuronClass, Type signal)
    {
        ArgumentNullException.ThrowIfNull(neuronClass);
        ArgumentNullException.ThrowIfNull(signal);

        return neuronClass.IsCollectible || signal.IsCollectible
            ? Lookup((neuronClass, signal))
            : Reactions.GetOrAdd((neuronClass, signal), Lookup);
    }

    /// <summary>Runs the reaction of <paramref name="neuron"/> to <paramref name="signal"/>; returns false when it has none.</summary>
    public static async Task<bool> React(Neuron neuron, Signal signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(neuron);
        ArgumentNullException.ThrowIfNull(signal);

        if (Find(neuron.GetType(), signal.GetType()) is not { } reaction) { return false; }
        await Invoke(reaction, neuron, signal, cancellationToken).ConfigureAwait(true);
        return true;
    }

    /// <summary>Invokes a reaction method safely, unwrapping TargetInvocationException and awaiting Tasks/ValueTasks.</summary>
    public static async Task Invoke(MethodInfo reaction, Neuron neuron, Signal signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reaction);
        ArgumentNullException.ThrowIfNull(neuron);
        ArgumentNullException.ThrowIfNull(signal);

        var arguments = reaction.GetParameters().Length == 2
            ? new object?[] { signal, cancellationToken }
            : [signal];

        object? returned;
        try
        {
            returned = reaction.Invoke(neuron, arguments);
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }

        if (returned is Task task)
        {
            await task.ConfigureAwait(true);
        }
        else if (returned is ValueTask valueTask)
        {
            await valueTask.ConfigureAwait(true);
        }
    }

    /// <summary>Resolves the journal alias of a signal type.</summary>
    public static string ResolveAlias(Type signalType) => SignalAliases.Of(signalType);

    /// <summary>Resolves the journal alias of a signal instance.</summary>
    public static string ResolveAlias(Signal signal) => SignalAliases.Of(signal);

    /// <summary>Whether <paramref name="signal"/> matches the specified alias.</summary>
    public static bool MatchesAlias(string alias, Signal signal)
    {
        ArgumentNullException.ThrowIfNull(alias);
        ArgumentNullException.ThrowIfNull(signal);
        return string.Equals(SignalAliases.Of(signal), alias, StringComparison.Ordinal);
    }

    /// <summary>Determines whether an incoming signal matches any subscribed types or aliases.</summary>
    public static bool Wants(IEnumerable<Type> subscribedSignals, IEnumerable<string> subscribedAliases, Signal signal)
    {
        ArgumentNullException.ThrowIfNull(subscribedSignals);
        ArgumentNullException.ThrowIfNull(subscribedAliases);
        ArgumentNullException.ThrowIfNull(signal);

        if (subscribedSignals.Any(type => type.IsInstanceOfType(signal))) { return true; }

        var signalAlias = SignalAliases.Of(signal);
        var aliasesList = subscribedAliases as IReadOnlyCollection<string> ?? subscribedAliases.ToList();
        if (aliasesList.Count > 0 && aliasesList.Contains(signalAlias, StringComparer.Ordinal))
        {
            return true;
        }

        return subscribedSignals.Any(type => string.Equals(SignalAliases.Of(type), signalAlias, StringComparison.Ordinal));
    }

    /// <summary>Deterministically sorts synapse records by From, To, and Signal using ordinal string comparison.</summary>
    public static IReadOnlyList<T> SortSynapses<T>(
        IEnumerable<T> synapses,
        Func<T, string> getFrom,
        Func<T, string> getTo,
        Func<T, string> getSignal)
    {
        ArgumentNullException.ThrowIfNull(synapses);
        ArgumentNullException.ThrowIfNull(getFrom);
        ArgumentNullException.ThrowIfNull(getTo);
        ArgumentNullException.ThrowIfNull(getSignal);

        return synapses
            .OrderBy(getFrom, StringComparer.Ordinal)
            .ThenBy(getTo, StringComparer.Ordinal)
            .ThenBy(getSignal, StringComparer.Ordinal)
            .ToList();
    }

    private static MethodInfo? Lookup((Type Class, Type Signal) key)
    {
        var candidates = new List<MethodInfo>();
        for (var type = key.Class; type is not null && type != typeof(object); type = type.BaseType)
        {
            candidates.AddRange(type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.IsDefined(typeof(OnAttribute), false)
                    && method.GetParameters() is { Length: 1 or 2 } parameters
                    && parameters[0].ParameterType.IsAssignableFrom(key.Signal)
                    && (parameters.Length == 1 || parameters[1].ParameterType == typeof(CancellationToken))));
        }

        return candidates
            .OrderByDescending(method => Depth(method.GetParameters()[0].ParameterType))
            .ThenBy(method => method.Name, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static int Depth(Type type)
    {
        var depth = 0;
        for (var current = type; current is not null; current = current.BaseType) { depth++; }
        return depth;
    }
}
