using Orleans.Journaling;

namespace DigitalBrain.Runtime.Neurons;

/// <summary>
/// Stateful base class implementing INeuronOfT and inheriting from Neuron.
/// </summary>
public abstract class Neuron<TState> : Neuron, INeuron<TState>
    where TState : new()
{
    private TState _state = new();

    public TState State
    {
        get => _state;
        set => _state = value;
    }

    protected Neuron(
        IDurableList<Synapse> incoming,
        IDurableList<Synapse> outgoing,
        IGrainFactory grains,
        ILogger logger)
        : base(incoming, outgoing, grains, logger)
    {
    }

    protected Neuron()
    {
    }

    public virtual Task OnActivatedAsync()
    {
        return Task.CompletedTask;
    }

    public virtual Task OnDeactivatedAsync()
    {
        return Task.CompletedTask;
    }

    public virtual Task<Synapse> OnSynapseReceivedAsync(Synapse synapse)
    {
        return Task.FromResult(synapse);
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        await OnActivatedAsync();
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        await OnDeactivatedAsync();
        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    protected async Task SaveStateAsync(CancellationToken ct = default)
    {
        await WriteStateAsync(ct);
    }
}
