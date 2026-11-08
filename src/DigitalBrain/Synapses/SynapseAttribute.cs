namespace DigitalBrain;

/// <summary>
/// Marks a state-neuron method as a synapse: a gate or reaction (<c>from</c> is a signal type) or a view (<c>from</c> is the state alias).
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SynapseAttribute : Attribute
{
    private const string DefaultKey = "default";

    /// <summary>Declares a gate or reaction fed by signals of type <paramref name="from"/> into state <paramref name="into"/>.</summary>
    /// <remarks>
    /// A gate's state can lag one signal behind: the publisher's journal entry and the gate's state entry are not one transaction.
    /// The host appends the publisher's signal first and only then saves the gate's allowed next state (<c>Committed</c>); if the
    /// process dies between the two, the signal is published but the gate never counted it. For a cap this means at most one charge
    /// under-counted after a crash. A gate that cannot accept this must not rely on its own state alone (for example, recount from
    /// the publisher's journal).
    /// </remarks>
    /// <param name="from">The <see cref="DigitalBrain.Signal"/> type that arrives.</param>
    /// <param name="into">The target state: <c>{alias}</c> or <c>{alias}/{key}</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="from"/> is not a <see cref="DigitalBrain.Signal"/> type, or the alias or the key after the slash is empty.</exception>
    public SynapseAttribute(Type from, string into)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(into);
        if (!typeof(DigitalBrain.Signal).IsAssignableFrom(from))
        {
            throw new ArgumentException($"{from.FullName} is not a Signal type.", nameof(from));
        }

        Signal = from;
        (State, Key) = Target(into);
    }

    /// <summary>
    /// Declares a reaction fed by signals with the journal alias <paramref name="signalAlias"/> into state <paramref name="into"/>; the
    /// function takes a <see cref="SignalEnvelope"/>, so it needs no reference to the signal's type.
    /// </summary>
    /// <param name="signalAlias">The <c>[Signal]</c> alias of the arriving signal.</param>
    /// <param name="into">The target state: <c>{alias}</c> or <c>{alias}/{key}</c>.</param>
    /// <exception cref="ArgumentException">The signal alias is empty, or the state alias or the key after the slash is empty.</exception>
    public SynapseAttribute(string signalAlias, string into)
    {
        ArgumentException.ThrowIfNullOrEmpty(signalAlias);
        ArgumentNullException.ThrowIfNull(into);
        SignalAlias = signalAlias;
        (State, Key) = Target(into);
    }

    private static (string State, string Key) Target(string into)
    {
        var slash = into.IndexOf('/', StringComparison.Ordinal);
        var state = slash < 0 ? into : into[..slash];
        var key = slash < 0 ? DefaultKey : into[(slash + 1)..];
        if (state.Length == 0)
        {
            throw new ArgumentException("The state alias must not be empty.", nameof(into));
        }

        if (key.Length == 0)
        {
            throw new ArgumentException("The state key after the slash must not be empty.", nameof(into));
        }

        return (state, key);
    }

    /// <summary>Declares a view over the state with alias <paramref name="from"/>.</summary>
    /// <param name="from">The state alias.</param>
    public SynapseAttribute(string from)
    {
        ArgumentException.ThrowIfNullOrEmpty(from);
        State = from;
    }

    /// <summary>The arriving signal type, or null for a view or an alias reaction.</summary>
    public Type? Signal { get; }

    /// <summary>The journal alias of the arriving signal of an alias reaction; null otherwise. Never the state alias.</summary>
    public string? SignalAlias { get; }

    /// <summary>The state alias.</summary>
    public string State { get; }

    /// <summary>The state key; <c>default</c> unless the target names one.</summary>
    public string Key { get; } = DefaultKey;

    /// <summary>A schedule that fires the reaction; valid only on a reaction from <see cref="DigitalBrain.Time.Tick"/>. Its text is validated by the shape reader.</summary>
    public string? Every { get; init; }
}
