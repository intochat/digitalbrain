namespace DigitalBrain.Rules;

/// <summary>
/// The verdict of a gate synapse: allow with the next state, or refuse with the broken rules. The allowed next state is saved only
/// after the publisher's append, so after a process death between the two the gate's state lags one signal behind (see
/// <see cref="SynapseAttribute(Type, string)"/>).
/// </summary>
public abstract record Check
{
    private protected Check() { }

    /// <summary>Allows the signal and proposes <paramref name="next"/> as the new state.</summary>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <param name="next">The proposed state.</param>
    /// <returns>An allowing verdict.</returns>
    public static Check Allow<TState>(TState next) => new Allowed(next!);

    /// <summary>Refuses the signal with one rule carrying <paramref name="reason"/>.</summary>
    /// <param name="reason">What a person is told.</param>
    /// <returns>A refusing verdict.</returns>
    public static Check Reject(string reason) => new Refused([new Reason("gate", reason)]);

    /// <summary>Refuses the signal with the given broken rules.</summary>
    /// <param name="broken">The broken rules.</param>
    /// <returns>A refusing verdict.</returns>
    public static Check Reject(params Rule[] broken) => new Refused(broken);

    /// <summary>The signal is allowed.</summary>
    /// <param name="Next">The proposed state.</param>
    public sealed record Allowed(object Next) : Check;

    /// <summary>The signal is refused.</summary>
    /// <param name="Broken">The broken rules.</param>
    public sealed record Refused(Rule[] Broken) : Check;

    internal sealed record Reason(string Name, string Text) : Rule(Name)
    {
        public override bool Broken => true;

        public override string Message => Text;
    }
}
