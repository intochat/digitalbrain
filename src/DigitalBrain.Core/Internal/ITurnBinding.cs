namespace DigitalBrain;

internal interface ITurnBinding
{
    NeuronId Id { get; }

    void Stage(Synapse synapse);

    TState GetState<TState>()
        where TState : class, new();

    void SetState<TState>(TState state)
        where TState : class, new();
}
