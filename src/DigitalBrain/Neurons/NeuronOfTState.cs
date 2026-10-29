namespace DigitalBrain;

/// <summary>
/// A neuron with persisted state. State is immutable to user code: the only way to change it is
/// <c>Save(next, changed)</c> or <c>Save(next, signals)</c>, which write the state and its signals as one journal entry.
/// There is no save without a signal; <c>Publish</c> alone is for stateless reactions.
/// </summary>
/// <typeparam name="TState">An immutable state record with a parameterless constructor for the empty state.</typeparam>
public abstract class Neuron<TState> : Neuron where TState : class, new()
{
    /// <summary>The current state; loaded by the runtime before <c>OnActivate</c>.</summary>
    protected TState State { get; private set; } = new();

    internal override async Task Load()
        => State = await Context.State.Load<TState>().ConfigureAwait(true) ?? new TState();

    /// <summary>Saves <paramref name="next"/> with the signal that says what changed and, optionally, the signal that says what was acted on.</summary>
    protected Task Save(TState next, Signal changed, Signal? acted = null)
    {
        ArgumentNullException.ThrowIfNull(changed);
        return Save(next, acted is null ? [changed] : [changed, acted]);
    }

    /// <summary>Saves <paramref name="next"/> and publishes <paramref name="signals"/>, in order, as one journal entry.</summary>
    /// <exception cref="ArgumentException"><paramref name="signals"/> is empty or holds null.</exception>
    protected async Task Save(TState next, IReadOnlyList<Signal> signals)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(signals);
        if (signals.Count == 0) { throw new ArgumentException("A state change publishes at least one signal.", nameof(signals)); }
        if (signals.Any(signal => signal is null)) { throw new ArgumentException("A signal is null.", nameof(signals)); }
        await Context.State.Save(next, signals).ConfigureAwait(true);
        State = next;
        OnCommitted();
    }

    /// <summary>Runs after a save has succeeded, before the caller of Save resumes.</summary>
    protected virtual void OnCommitted() { }
}
