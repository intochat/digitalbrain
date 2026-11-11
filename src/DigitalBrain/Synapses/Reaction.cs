using DigitalBrain.Units;

namespace DigitalBrain;

/// <summary>The outcome of a reaction synapse: the next state and the signals to publish with it.</summary>
/// <typeparam name="TState">The state type.</typeparam>
/// <param name="Next">The next state.</param>
/// <param name="Signals">The signals saved atomically with the state.</param>
public sealed record Reaction<TState>(TState Next, IReadOnlyList<Signal> Signals)
{
    /// <summary>What the unit asks the kernel to do; recorded with the state in the same journal entry.</summary>
    public IReadOnlyList<Ask> Asks { get; init; } = [];

    /// <summary>Creates a reaction.</summary>
    /// <param name="next">The next state.</param>
    /// <param name="signals">The signals to publish.</param>
    /// <returns>The reaction.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Reaction<T>.Of is the specified factory; T is inferred from the first argument.")]
    public static Reaction<TState> Of(TState next, params Signal[] signals) => new(next, signals);

    /// <summary>Appends one ask to the reaction.</summary>
    /// <param name="ask">The ask to append.</param>
    /// <returns>A copy of the reaction with <paramref name="ask"/> appended to <see cref="Asks"/>.</returns>
    public Reaction<TState> Ask(Ask ask) => this with { Asks = [.. Asks, ask] };

    /// <summary>Appends one ask for each item.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The items to build asks from.</param>
    /// <param name="ask">Builds an ask from one item.</param>
    /// <returns>A copy of the reaction with the built asks appended to <see cref="Asks"/>.</returns>
    public Reaction<TState> AskEach<T>(IEnumerable<T> items, Func<T, Ask> ask) => this with { Asks = [.. Asks, .. items.Select(ask)] };
}
