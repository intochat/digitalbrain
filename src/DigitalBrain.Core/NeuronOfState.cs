using System.Text.Json;

namespace DigitalBrain;

public abstract class Neuron<TState> : Neuron
    where TState : class, new()
{
    private TState? working;
    private bool touched;

    protected TState State
    {
        get
        {
            if (!touched)
            {
                working = MaterializeState<TState>();
                touched = true;
            }

            return working!;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            touched = true;
            working = value;
        }
    }

    private protected override JsonElement? StateSlotIfTouched()
        => touched ? EncodeState(working!) : null;

    private protected override void ResetTurnState()
    {
        working = null;
        touched = false;
    }
}
