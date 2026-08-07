using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Kernel;

namespace DigitalBrain.TestingTests.Harness;

[ClientEntryPoint]
[Alias("harness.echo")]
[Description("Harness echo neuron for directed request/reply proofs")]
public partial interface IEcho : INeuron
{
    [Alias(nameof(Redeliver))]
    Task Redeliver(SynapseDelivery delivery);
}

[GenerateSerializer]
[Alias("harness.echo-request")]
[Description("Echo request text")]
public sealed record EchoRequest([property: Id(0)] string Text) : RequestSynapse<EchoResponse>;

[GenerateSerializer]
[Alias("harness.echo-response")]
[Description("Echo response text")]
public sealed record EchoResponse([property: Id(0)] string Text) : Synapse;

[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Orleans grain activated by the test silo from GrainType metadata.")]
internal sealed class Echo :
    Neuron,
    IEcho,
    IHandle<EchoRequest>,
    IEmit<EchoResponse>
{
    public Task HandleAsync(EchoRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ReplyAsync(new EchoResponse(request.Text), cancellationToken);
    }

    public Task Redeliver(SynapseDelivery delivery)
        => Deliver(delivery);
}

[ClientEntryPoint]
[Alias("harness.relay")]
[Description("Relays a directed request from inside its own grain turn")]
public partial interface IRelay : INeuron
{
    [Alias(nameof(RelayEcho))]
    Task<string> RelayEcho(string echoName, string text);
}

[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Orleans grain activated by the test silo from GrainType metadata.")]
internal sealed class Relay : Neuron, IRelay
{
    [SuppressMessage(
        "Usage",
        "CA1849:Call async methods when in an async method",
        Justification = "DigitalBrainClient.Connect is the in-silo factory; ConnectAsync is the behavior-worker surface.")]
    public async Task<string> RelayEcho(string echoName, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(echoName);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var brain = DigitalBrainClient.Connect(GrainFactory, Id.Owner.Value);
        var response = await brain.SendRequestAsync(
            NeuronId.For<IEcho>(Id.Owner, echoName),
            new EchoRequest(text),
            typeof(EchoResponse));
        return ((EchoResponse)response).Text;
    }
}

[ClientEntryPoint]
public partial interface IReplyProbe : INeuron
{
    [Alias(nameof(ReplyOutsideContext))]
    Task ReplyOutsideContext();
}

[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Orleans grain activated by the test silo from GrainType metadata.")]
internal sealed class ReplyProbe : Neuron, IReplyProbe
{
    public Task ReplyOutsideContext()
        => ReplyAsync(new EchoResponse("outside"));
}
