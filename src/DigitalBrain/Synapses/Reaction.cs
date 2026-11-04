namespace DigitalBrain;

/// <summary>The outcome of a reaction synapse: the next state and the signals to publish with it.</summary>
/// <typeparam name="TState">The state type.</typeparam>
/// <param name="Next">The next state.</param>
/// <param name="Signals">The signals saved atomically with the state.</param>
public sealed record Reaction<TState>(TState Next, IReadOnlyList<Signal> Signals)
{
    /// <summary>Creates a reaction.</summary>
    /// <param name="next">The next state.</param>
    /// <param name="signals">The signals to publish.</param>
    /// <returns>The reaction.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Reaction<T>.Of is the specified factory; T is inferred from the first argument.")]
    public static Reaction<TState> Of(TState next, params Signal[] signals) => new(next, signals);
}
