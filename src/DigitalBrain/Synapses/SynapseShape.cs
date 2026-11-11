using System.Reflection;
using DigitalBrain.Capabilities;
using DigitalBrain.Time;

namespace DigitalBrain.Units;

/// <summary>What a synapse function does with the signal and the state.</summary>
public enum SynapseKind
{
    /// <summary>Computes the next state, optionally with signals to publish.</summary>
    Reaction = 1,

    /// <summary>Projects the state to a value; takes only the state.</summary>
    View = 2,
}

/// <summary>A synapse function read into its shape.</summary>
/// <param name="Kind">Reaction or view.</param>
/// <param name="Function">The compiled function.</param>
/// <param name="Signal">The signal parameter type; null for a view.</param>
/// <param name="State">The state record type.</param>
/// <param name="StateAlias">The state alias from the attribute.</param>
/// <param name="Key">The state key from the attribute.</param>
/// <param name="TakesCancellation">Whether the function ends with a <see cref="CancellationToken"/>.</param>
/// <param name="Slots">The capability slots the function takes, in parameter order.</param>
/// <param name="Every">The schedule of a <see cref="Time.Tick"/> reaction; null otherwise.</param>
public sealed record SynapseDescriptor(
    SynapseKind Kind,
    MethodInfo Function,
    Type? Signal,
    Type State,
    string StateAlias,
    string Key,
    bool TakesCancellation,
    IReadOnlyList<SlotDescriptor> Slots,
    Schedule? Every)
{
    /// <summary>The journal alias an alias reaction is fed by (its <see cref="Signal"/> is <see cref="SignalEnvelope"/>); null otherwise.</summary>
    public string? SignalAlias { get; init; }

    /// <summary>True when the function takes an <see cref="Answer"/>: the kernel calls it once per answer of an <see cref="Answered"/> signal and saves once.</summary>
    public bool PerAnswer { get; init; }

    /// <summary>The qualified function name.</summary>
    public string Name => Function.DeclaringType!.FullName + "." + Function.Name;
}

/// <summary>A capability slot parameter of a synapse function.</summary>
/// <param name="Name">The slot name: the <c>[Slot]</c> name or the parameter name.</param>
/// <param name="Contract">The capability interface type.</param>
/// <param name="Capability">The capability the contract declares.</param>
/// <param name="ParameterIndex">The zero-based parameter position.</param>
public sealed record SlotDescriptor(string Name, Type Contract, CapabilityId Capability, int ParameterIndex)
{
    /// <summary>True for an <c>Asks&lt;TContract&gt;</c> slot: the function gets a handle that builds asks, and the capability is resolved only when the kernel carries an ask out.</summary>
    public bool Deferred { get; init; }
}

/// <summary>The refusal fragments <see cref="SynapseShape"/> puts in its messages, shared so analyzers can mirror them.</summary>
public static class SynapseReasons
{
    /// <summary>A reaction has no signal parameter.</summary>
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

    /// <summary>The signal type declared by <c>[Synapse]</c> is not assignable to the signal parameter.</summary>
    public const string SignalFitsParameter = "declared signal type must be assignable to the signal parameter";

    /// <summary>A <c>[Slot]</c> parameter whose type is not an interface carrying <c>[Capability]</c>.</summary>
    public const string SlotContractNeedsCapability = "slot contract needs [Capability]";

    /// <summary>The <c>Every</c> text is not a schedule; followed by <c>: </c> and the text.</summary>
    public const string Schedule = "schedule";

    /// <summary><c>Every</c> on anything but a reaction to <c>Tick</c>.</summary>
    public const string EveryNeedsTick = "every needs a Tick reaction";

    /// <summary>An alias reaction whose signal parameter is not <see cref="SignalEnvelope"/>, or an alias synapse that is not a reaction.</summary>
    public const string AliasNeedsEnvelope = "alias reaction takes a SignalEnvelope, not a signal type";

    /// <summary>The catalog found one slot name bound to two contracts.</summary>
    public const string SlotBoundTwice = "slot";

    /// <summary>An <c>Asks&lt;&gt;</c> slot on a function that is not a synchronous reaction.</summary>
    public const string AsksNeedSyncReaction = "Asks<> slot needs a synchronous reaction";

    /// <summary>An <c>Asks&lt;&gt;</c> slot and a calling <c>[Slot]</c> on the same function.</summary>
    public const string AsksExcludeCallingSlot = "Asks<> slot cannot share a function with a calling slot";

    /// <summary>An <see cref="Answer"/> parameter outside a synchronous reaction to <see cref="Answered"/> without calling slots, or more than one <see cref="Answer"/> parameter.</summary>
    public const string AnswerNeedsAnswered = "Answer parameter needs a synchronous reaction to Answered";
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
        var envelopes = 0;
        var stateCount = 0;
        var tokens = 0;
        var answers = 0;
        var unknown = false;
        var parameters = function.GetParameters();
        var misplacedToken = false;
        var slots = new List<SlotDescriptor>();
        for (var i = 0; i < parameters.Length; i++)
        {
            var type = parameters[i].ParameterType;
            if (parameters[i].GetCustomAttribute<SlotAttribute>() is { } slot)
            {
                var deferred = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Asks<>);
                var contract = deferred ? type.GetGenericArguments()[0] : type;
                var capability = contract.IsInterface ? contract.GetCustomAttribute<CapabilityAttribute>() : null;
                if (capability is null)
                {
                    throw new SynapseShapeException($"{name}: a {SynapseReasons.SlotContractNeedsCapability} (parameter '{parameters[i].Name}').");
                }

                slots.Add(new SlotDescriptor(slot.Name ?? parameters[i].Name!, contract, capability.Id, i) { Deferred = deferred });
            }
            else if (typeof(Signal).IsAssignableFrom(type))
            {
                signals++;
                signal = type;
            }
            else if (type == typeof(SignalEnvelope) && attribute.SignalAlias is not null)
            {
                envelopes++;
            }
            else if (type == typeof(CancellationToken))
            {
                tokens++;
                misplacedToken |= i != parameters.Length - 1;
            }
            else if (type == typeof(Answer))
            {
                answers++;
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

        if (attribute.SignalAlias is not null && (signals > 0 || envelopes != 1))
        {
            throw new SynapseShapeException($"{name}: an {SynapseReasons.AliasNeedsEnvelope}.");
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
        var kind = returned == state || returned == typeof(Reaction<>).MakeGenericType(state) ? SynapseKind.Reaction
            : SynapseKind.View;
        var isAsync = returned != function.ReturnType;
        var hasDeferred = slots.Any(s => s.Deferred);
        var hasCalling = slots.Any(s => !s.Deferred);
        if (hasDeferred && (kind != SynapseKind.Reaction || isAsync || tokens > 0))
        {
            throw new SynapseShapeException($"{name}: an {SynapseReasons.AsksNeedSyncReaction}.");
        }

        if (hasDeferred && hasCalling)
        {
            throw new SynapseShapeException($"{name}: an {SynapseReasons.AsksExcludeCallingSlot}.");
        }

        if (answers > 0 && (answers > 1 || attribute.Signal != typeof(Answered) || kind != SynapseKind.Reaction || isAsync || tokens > 0 || hasCalling))
        {
            throw new SynapseShapeException($"{name}: an {SynapseReasons.AnswerNeedsAnswered}.");
        }

        if (attribute.SignalAlias is { } signalAlias)
        {
            if (kind != SynapseKind.Reaction)
            {
                throw new SynapseShapeException($"{name}: an {SynapseReasons.AliasNeedsEnvelope}; an alias synapse must return the state.");
            }

            if (attribute.Every is not null)
            {
                throw new SynapseShapeException($"{name}: {SynapseReasons.EveryNeedsTick}.");
            }

            return new SynapseDescriptor(kind, function, typeof(SignalEnvelope), state, alias, attribute.Key, tokens == 1, slots, null) { SignalAlias = signalAlias, PerAnswer = answers == 1 };
        }

        if (kind == SynapseKind.View)
        {
            if (signal is not null || tokens > 0 || attribute.Signal is not null)
            {
                throw new SynapseShapeException($"{name}: a {SynapseReasons.ViewTakesOnlyState}; a function with a signal must return the state.");
            }
        }
        else if (signal is null || attribute.Signal is null)
        {
            throw new SynapseShapeException($"{name}: a {SynapseReasons.GateNeedsSignal} (a reaction as well), declared by [Synapse] and taken as a parameter.");
        }

        if (kind != SynapseKind.View && !signal!.IsAssignableFrom(attribute.Signal))
        {
            throw new SynapseShapeException($"{name}: the {SynapseReasons.SignalFitsParameter}.");
        }

        Schedule? every = null;
        if (attribute.Every is { } text)
        {
            if (kind != SynapseKind.Reaction || attribute.Signal != typeof(Tick))
            {
                throw new SynapseShapeException($"{name}: {SynapseReasons.EveryNeedsTick}.");
            }

            try
            {
                every = Schedule.Parse(text);
            }
            catch (FormatException)
            {
                throw new SynapseShapeException($"{name}: a {SynapseReasons.Schedule}: {text} is not HH:mm, Mon HH:mm, every 5m or every 2h.");
            }
        }

        return new SynapseDescriptor(kind, function, signal, state, alias, attribute.Key, tokens == 1, slots, every) { PerAnswer = answers == 1 };
    }

    private static Type Unwrap(Type returned) =>
        returned.IsGenericType && returned.GetGenericTypeDefinition() == typeof(Task<>) ? returned.GetGenericArguments()[0] : returned;
}
