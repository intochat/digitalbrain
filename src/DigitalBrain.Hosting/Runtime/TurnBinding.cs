using System.Text.Json;

namespace DigitalBrain;

internal sealed class TurnBinding(
    NeuronId id,
    SynapseOrigin origin,
    Journal journal,
    ISynapseSerialization serialization) : ITurnBinding
{
    private readonly List<StagedSynapse> staged = [];
    private object? state;
    private Type? stateType;
    private bool stateTouched;

    public NeuronId Id { get; } = id;

    public SynapseOrigin Origin { get; } = origin;

    internal IReadOnlyList<StagedSynapse> Staged => staged;

    public void Stage(Synapse synapse, Dispatch dispatch)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        staged.Add(new StagedSynapse(synapse, dispatch));
    }

    public TState GetState<TState>()
        where TState : class, new()
    {
        EnsureStateType<TState>();
        if (!stateTouched)
        {
            var recorded = journal.RecordedState;
            state = recorded.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? new TState()
                : serialization.Deserialize(recorded, typeof(TState)) as TState ?? new TState();
            stateTouched = true;
        }

        return (TState)state!;
    }

    public void SetState<TState>(TState value)
        where TState : class, new()
    {
        ArgumentNullException.ThrowIfNull(value);
        EnsureStateType<TState>();
        state = value;
        stateTouched = true;
    }

    internal JsonElement? SerializeTouchedState()
        => stateTouched ? serialization.Serialize(state!) : null;

    private void EnsureStateType<TState>()
        where TState : class, new()
    {
        var requested = typeof(TState);
        if (stateType is not null && stateType != requested)
        {
            throw new InvalidOperationException(
                $"A turn may use one state type; it already uses {stateType.FullName}, not {requested.FullName}.");
        }

        stateType = requested;
    }
}
