namespace DigitalBrain;

public abstract class Neuron
{
    private ITurnBinding? binding;

    protected NeuronId Id => Binding.Id;

    protected SynapseOrigin Origin => Binding.Origin;

    protected void Emit(Synapse synapse, Dispatch dispatch = default)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        Binding.Stage(synapse, dispatch);
    }

    internal void Bind(ITurnBinding turnBinding)
    {
        ArgumentNullException.ThrowIfNull(turnBinding);
        if (binding is not null)
        {
            throw new InvalidOperationException("A behavior instance is already bound to a turn.");
        }

        binding = turnBinding;
    }

    internal void Unbind(ITurnBinding turnBinding)
    {
        ArgumentNullException.ThrowIfNull(turnBinding);
        if (!ReferenceEquals(binding, turnBinding))
        {
            throw new InvalidOperationException("A behavior instance can only unbind its active turn.");
        }

        binding = null;
    }

    private protected ITurnBinding Binding
        => binding ?? throw new InvalidOperationException("Behavior operations are valid only while handling a synapse.");
}
