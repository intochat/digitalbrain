using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;

namespace DigitalBrain.TestingTests;

#pragma warning disable CA1515 // Public probe types model an external consumer assembly.
public sealed partial class TestingProbeModule : IModule;

public partial interface IEchoNeuron : INeuron
{
    [Alias(nameof(Echo))]
    Task<string> Echo(string value);
}

internal sealed class EchoNeuron : Neuron, IEchoNeuron
{
    public Task<string> Echo(string value) => Task.FromResult(value);
}

public sealed class TestingFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<TestingProbeModule>();
    }
}
#pragma warning restore CA1515
