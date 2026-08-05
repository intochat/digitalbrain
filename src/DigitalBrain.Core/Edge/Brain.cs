namespace DigitalBrain;

public sealed class Brain(IGrainFactory grains)
{
    public Session Session(string context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        return new Session(grains, context);
    }

    public async Task<NeuronReading> ReadAsync(
        NeuronId neuron, long afterPosition = 0, CancellationToken cancellationToken = default)
        => await grains
            .GetGrain<Neuron.ITransport>(Neuron.AddressOf(neuron))
            .ReadAsync(afterPosition)
            .WaitAsync(cancellationToken);
}
