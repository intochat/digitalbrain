namespace DigitalBrain;

internal interface ITurnBinding
{
    NeuronId Id { get; }

    SynapseOrigin Origin { get; }

    void Stage(Synapse synapse, Dispatch dispatch);

    TState GetState<TState>()
        where TState : class, new();

    void SetState<TState>(TState state)
        where TState : class, new();
}
