namespace DigitalBrain;

public sealed class EdgeSession
{
    private readonly IGrainFactory grains;

    internal EdgeSession(IGrainFactory grains, string context)
    {
        this.grains = grains;
        Id = Ingress.IdFor(context);
    }

    public NeuronId Id { get; }

    public Task EmitAsync(Synapse fact, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fact);
        cancellationToken.ThrowIfCancellationRequested();
        return grains.GetGrain<IIngress>(Neuron.AddressOf(Id)).EmitAsync(fact);
    }
}
