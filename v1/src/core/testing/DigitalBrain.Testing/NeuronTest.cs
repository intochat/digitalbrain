using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

public abstract class NeuronTest<TNeuron> : DigitalBrainTest
    where TNeuron : class, INeuron
{
    protected async ValueTask<TestNeuron<TNeuron>> NeuronAsync(string name = "default")
        => (await BrainAsync()).Neuron<TNeuron>(name);
}
