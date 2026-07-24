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

    [Alias(nameof(Publish))]
    Task Publish(string value);
}

[GenerateSerializer]
[Alias("tests.echo-requested")]
internal sealed record EchoRequested([property: Id(0)] string Value) : Synapse;

[GenerateSerializer]
[Alias("tests.echoed")]
internal sealed record Echoed([property: Id(0)] string Value) : Synapse;

internal sealed class EchoNeuron :
    Neuron,
    IEchoNeuron,
    IHandle<EchoRequested>,
    IEmit<Echoed>
{
    public Task<string> Echo(string value) => Task.FromResult(value);

    public Task Publish(string value) => EmitAsync(new Echoed(value));

    public Task HandleAsync(
        EchoRequested request,
        CancellationToken cancellationToken)
        => EmitAsync(new Echoed(request.Value));
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
