using System.Text.Json;

namespace DigitalBrain;

// One generic parameter is the entire module state surface (§6): a working copy per turn,
// lazily materialized from the committed slot on first access, committed only if accessed,
// discarded unconditionally at turn end. Instance fields on a neuron are volatile and die
// with the activation — TState is the only durable module state. The TState contract
// (default-constructible, codec-round-trippable, no required members) is boot-checked by
// BodyCodec.ValidateState from the hosting seam (AddDigitalBrain), not here.
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
