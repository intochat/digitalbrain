namespace DigitalBrain.Neurons;

/// <summary>
/// The seat of a hosted neuron: its save path and the services its constructor resolved from. Internal: only the core's own base
/// classes (Neuron{TState}, Screen{TState}, Flow{T}) and the runtimes see it, so module code has no save without a signal and
/// no service locator. HostNeuronContext, TestNeuronContext and FakeNeuronContext implement it beside INeuronContext.
/// </summary>
internal interface INeuronSeat
{
    /// <summary>The stored state, or default when none exists.</summary>
    Task<TState?> Load<TState>() where TState : class;

    /// <summary>Stores <paramref name="next"/> and journals <paramref name="signals"/>, in order, as one journal entry.</summary>
    Task Save<TState>(TState next, IReadOnlyList<Signal> signals) where TState : class;

    /// <summary>Deletes the stored state.</summary>
    Task Clear();

    /// <summary>The <see cref="NeuronServices"/> this neuron was constructed from.</summary>
    IServiceProvider Services { get; }
}
