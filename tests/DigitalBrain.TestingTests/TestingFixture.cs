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

[ClientEntryPoint]
public partial interface ICapabilityRetryDriver : INeuron
{
    [Alias(nameof(PublishWithRetry))]
    Task PublishWithRetry(NeuronId target, string value);
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

internal sealed class CapabilityRetryDriver :
    Neuron,
    ICapabilityRetryDriver
{
    public async Task PublishWithRetry(NeuronId target, string value)
    {
        var echo = GrainFactory.GetGrain<IEchoNeuron>(
            $"{target.Owner.Value}/{target.Name}");

        try
        {
            await echo.Publish(value);
        }
        catch (InvalidOperationException)
        {
        }

        await echo.Publish(value);
    }
}

public sealed class TestingFixture : DigitalBrainFixture
{
    private DigitalBrainTestBuilder? _composition;

    internal EdgeScriptProbe EdgeScript { get; } = new();

    internal McpScriptProbe McpScript { get; } = new();

    internal void AddProbeModuleAfterInitialization()
        => Composition().AddModule<TestingProbeModule>();

    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        _composition = brain;
        brain.AddModule<TestingProbeModule>();
        brain.ConfigureProbeChat(EdgeScript);
        brain.ConfigureProbeMcp(McpScript);
    }

    private DigitalBrainTestBuilder Composition()
        => _composition
            ?? throw new InvalidOperationException(
                "The test fixture composition has not been initialized.");
}
#pragma warning restore CA1515
