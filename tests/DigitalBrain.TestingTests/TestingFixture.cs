using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;

namespace DigitalBrain.TestingTests;

#pragma warning disable CA1515 // Public probe types model an external consumer assembly.
public sealed partial class TestingProbeModule : IModule;

[ClientEntryPoint]
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
    private DigitalBrainTestBuilder? _composition;

    internal void AddProbeModuleAfterInitialization()
        => Composition().AddModule<TestingProbeModule>();

    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        _composition = brain;
        brain.AddModule<TestingProbeModule>();
    }

    private DigitalBrainTestBuilder Composition()
        => _composition
            ?? throw new InvalidOperationException(
                "The test fixture composition has not been initialized.");
}
#pragma warning restore CA1515
