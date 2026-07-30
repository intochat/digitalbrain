using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.TestingTests.Harness;

[ClientEntryPoint]
public partial interface IEcho : INeuron
{
    [Alias(nameof(Redeliver))]
    Task Redeliver(SynapseDelivery delivery);
}

[GenerateSerializer]
[Alias("harness.echo-request")]
public sealed record EchoRequest([property: Id(0)] string Text) : RequestSynapse<EchoResponse>;

[GenerateSerializer]
[Alias("harness.echo-response")]
public sealed record EchoResponse([property: Id(0)] string Text) : Synapse;

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
public partial interface IReplyProbe : INeuron
{
    [Alias(nameof(ReplyOutsideContext))]
    Task ReplyOutsideContext();
}

internal sealed class ReplyProbe : Neuron, IReplyProbe
{
    public Task ReplyOutsideContext()
        => ReplyAsync(new EchoResponse("outside"));
}
