namespace DigitalBrain;

internal sealed class OrleansSynapsePublisher(IGrainFactory grains) : SynapsePublisher
{
    public async Task PublishAsync(
        SynapseSource source,
        Synapse synapse,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var host = grains.GetGrain<INeuronHost>(NeuronHost.AddressOf(SynapseSourceIdentity.For(source)));
        await host.PublishAsync(synapse).WaitAsync(cancellationToken);
    }
}
