namespace DigitalBrain.Testing;

// The one-kind scope of DigitalBrainTest (§11): the composition is TNeuron alone unless
// the test overrides Compose, and NeuronAsync reads an instance's journal by name without
// hand-minting the kind — typed references survive only as journal-read sugar.
public abstract class NeuronTest<TNeuron>(BrainTestClusters clusters) : DigitalBrainTest(clusters)
    where TNeuron : Neuron
{
    protected static NeuronId Neuron(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new NeuronId(NeuronId.KindOf(typeof(TNeuron)), name);
    }

    protected override void Compose(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<TNeuron>();
    }

    protected Task<NeuronReading> NeuronAsync(string name, CancellationToken cancellationToken = default)
        => ReadAsync(Neuron(name), cancellationToken);
}
