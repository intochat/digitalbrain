using System.Reflection;

namespace DigitalBrain;

/// <summary>What a synapse function does with the signal and the state.</summary>
public enum SynapseKind
{
    /// <summary>Decides whether the signal may change the state: returns <c>Check</c>.</summary>
    Gate,

    /// <summary>Computes the next state, optionally with signals to publish.</summary>
    Reaction,

    /// <summary>Projects the state to a value; takes only the state.</summary>
    View,
}

/// <summary>A synapse function read into its shape.</summary>
/// <param name="Kind">Gate, reaction or view.</param>
/// <param name="Function">The compiled function.</param>
/// <param name="Signal">The signal parameter type; null for a view.</param>
/// <param name="State">The state record type.</param>
/// <param name="StateAlias">The state alias from the attribute.</param>
/// <param name="Key">The state key from the attribute.</param>
/// <param name="TakesCancellation">Whether the function ends with a <see cref="CancellationToken"/>.</param>
public sealed record SynapseDescriptor(SynapseKind Kind, MethodInfo Function, Type? Signal, Type State, string StateAlias, string Key, bool TakesCancellation)
{
    /// <summary>The qualified function name.</summary>
    public string Name => Function.DeclaringType!.FullName + "." + Function.Name;
}

/// <summary>The refusal fragments <see cref="SynapseShape"/> puts in its messages, shared so analyzers can mirror them.</summary>
public static class SynapseReasons
{
    /// <summary>A gate or reaction has no signal parameter.</summary>
    public const string GateNeedsSignal = "gate needs a signal";

    /// <summary>More than one signal parameter.</summary>
    public const string AtMostOneSignal = "at most one signal";

    /// <summary>Prefix of the missing or mismatched state record reason; followed by the quoted alias.</summary>
    public const string StateRecord = "state record";

    /// <summary>A view with a signal or a cancellation token.</summary>
    public const string ViewTakesOnlyState = "view takes only the state";

    /// <summary>The function is not public static.</summary>
    public const string PublicStatic = "public static";

    /// <summary>A parameter of an unsupported type.</summary>
    public const string UnknownParameter = "not a signal, the state or CancellationToken";
}

/// <summary>Reads the shape of a synapse function.</summary>
public static class SynapseShape
{
    /// <summary>Reads <paramref name="function"/> against the known state records.</summary>
    /// <param name="function">The method marked <c>[Synapse]</c>.</param>
    /// <param name="states">State alias to record type.</param>
    /// <returns>The descriptor.</returns>
    /// <exception cref="SynapseShapeException">The shape is not supported; the message says why.</exception>
    public static SynapseDescriptor Of(MethodInfo function, IReadOnlyDictionary<string, Type> states)
    {
        ArgumentNullException.ThrowIfNull(function);
        var attribute = function.GetCustomAttribute<SynapseAttribute>()
            ?? throw new SynapseShapeException($"{function.DeclaringType?.FullName}.{function.Name}: the function is not marked [Synapse].");
        return Of(function, states, attribute);
    }

    internal static SynapseDescriptor Of(MethodInfo function, IReadOnlyDictionary<string, Type> states, SynapseAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(states);
        var name = function.DeclaringType?.FullName + "." + function.Name;

        if (!function.IsPublic || !function.IsStatic)
        {
            throw new SynapseShapeException($"{name}: a synapse function must be {SynapseReasons.PublicStatic}.");
        }

        var owner = function.DeclaringType!;
        if (owner.IsNested && !(owner.DeclaringType!.IsAbstract && owner.DeclaringType.IsSealed))
        {
            throw new SynapseShapeException($"{name}: a synapse function must be {SynapseReasons.PublicStatic} on a static class or a top-level type, not nested in a non-static class.");
        }

        var alias = attribute.State;
        var state = states.TryGetValue(alias, out var known) ? known : null;
        Type? signal = null;
        var signals = 0;
        var stateCount = 0;
        var tokens = 0;
        var unknown = false;
        var parameters = function.GetParameters();
        var misplacedToken = false;
        for (var i = 0; i < parameters.Length; i++)
        {
            var type = parameters[i].ParameterType;
            if (typeof(Signal).IsAssignableFrom(type))
            {
                signals++;
                signal = type;
            }
            else if (type == typeof(CancellationToken))
            {
                tokens++;
                misplacedToken |= i != parameters.Length - 1;
            }
            else if (state is not null && type == state)
            {
                stateCount++;
            }
            else
            {
                unknown = true;
            }
        }

        if (signals > 1)
        {
            throw new SynapseShapeException($"{name}: a synapse takes {SynapseReasons.AtMostOneSignal}.");
        }

        if (state is null || stateCount != 1)
        {
            throw new SynapseShapeException($"{name}: a synapse takes exactly one {SynapseReasons.StateRecord} '{alias}' parameter.");
        }

        if (unknown || tokens > 1 || misplacedToken)
        {
            throw new SynapseShapeException($"{name}: a parameter is {SynapseReasons.UnknownParameter}.");
        }

        var returned = Unwrap(function.ReturnType);
        var kind = returned == typeof(Rules.Check) ? SynapseKind.Gate
            : returned == state || returned == typeof(Reaction<>).MakeGenericType(state) ? SynapseKind.Reaction
            : SynapseKind.View;

        if (kind == SynapseKind.View)
        {
            if (signal is not null || tokens > 0 || attribute.Signal is not null)
            {
                throw new SynapseShapeException($"{name}: a {SynapseReasons.ViewTakesOnlyState}; a function with a signal must return Check or the state.");
            }
        }
        else if (signal is null || attribute.Signal is null)
        {
            throw new SynapseShapeException($"{name}: a {SynapseReasons.GateNeedsSignal} (a reaction as well), declared by [Synapse] and taken as a parameter.");
        }

        return new SynapseDescriptor(kind, function, signal, state, alias, attribute.Key, tokens == 1);
    }

    private static Type Unwrap(Type returned) =>
        returned.IsGenericType && returned.GetGenericTypeDefinition() == typeof(Task<>) ? returned.GetGenericArguments()[0] : returned;
}
