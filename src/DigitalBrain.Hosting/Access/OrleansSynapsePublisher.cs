namespace DigitalBrain;

internal sealed class OrleansSynapsePublisher : SynapsePublisher
{
    private readonly IGrainFactory grains;
    private readonly ScopedNeuronAddress source;
    private readonly HashSet<Type> permittedIngressSynapses;

    internal OrleansSynapsePublisher(
        IGrainFactory grains,
        ScopedNeuronAddress source,
        IReadOnlySet<Type> permittedIngressSynapses)
    {
        this.grains = grains ?? throw new ArgumentNullException(nameof(grains));
        this.source = source;
        ArgumentNullException.ThrowIfNull(permittedIngressSynapses);
        this.permittedIngressSynapses = [.. permittedIngressSynapses];
    }

    public async Task PublishAsync(
        Synapse synapse,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        if (!permittedIngressSynapses.Contains(synapse.GetType()))
        {
            throw new InvalidOperationException(
                $"{synapse.GetType().FullName} is not permitted for this source channel.");
        }

        var host = grains.GetGrain<INeuronHost>(NeuronHost.AddressOf(source));
        await host.PublishAsync(synapse).WaitAsync(cancellationToken);
    }
}
