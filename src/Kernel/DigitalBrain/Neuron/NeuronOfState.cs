using Orleans.Runtime;

namespace DigitalBrain.Core;

// A neuron whose domain state is a snapshot (IPersistentState) rather than the op log.
// Concrete subclasses must redeclare [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)]
// on their own constructor parameter and forward it: Orleans binds facets on the leaf constructor.
public abstract class Neuron<TState> : Neuron where TState : class
{
    private readonly IPersistentState<TState> _state;

    protected Neuron(NeuronRuntime runtime, IPersistentState<TState> state) : base(runtime)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
    }

    protected TState? State => _state.RecordExists ? _state.State : null;

    protected Task SaveAsync(TState value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (ExecutingCommand is { } id)
        {
            throw new InvalidOperationException(
                $"Neuron '{Id}' cannot save a snapshot while executing command '{id}': save state from a reaction, not a command.");
        }

        _state.State = value;
        return _state.WriteStateAsync(cancellationToken);
    }
}
