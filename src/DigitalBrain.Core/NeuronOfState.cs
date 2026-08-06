namespace DigitalBrain;

public abstract class Neuron<TState> : Neuron
    where TState : class, new()
{
    protected TState State
    {
        get => Binding.GetState<TState>();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Binding.SetState(value);
        }
    }
}
