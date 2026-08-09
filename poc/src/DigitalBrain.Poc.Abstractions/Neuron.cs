namespace DigitalBrain.Poc.Abstractions;

public abstract class Neuron
{
    protected Neuron(IDigitalBrain digitalBrain)
    {
        DigitalBrain = digitalBrain;
    }

    protected IDigitalBrain DigitalBrain { get; }
}

public abstract class Neuron<TState> : Neuron
{
    protected Neuron(IDigitalBrain digitalBrain, IDurableState<TState> durableState)
        : base(digitalBrain)
    {
        DurableState = durableState;
    }

    protected IDurableState<TState> DurableState { get; }
}
