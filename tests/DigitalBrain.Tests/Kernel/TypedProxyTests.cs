using Brain.Client;
using Brain.Contracts;
using DigitalBrain.Tests;
using Xunit;

namespace Brain.KernelTests;

public interface ITestNeuron : INeuronContract
{
    [NeuronContract("test.echo.v1")]
    Task<EchoReply> EchoAsync(EchoRequest request);

    [NeuronContract("test.echo.v1")]
    Task<NeuronReply<EchoReply>> EchoWithReceiptAsync(EchoRequest request);
}

public sealed record EchoRequest(int V);
public sealed record EchoReply(int V);

public class TypedProxyTests(BrainClusterFixture<KernelKindsConfigurator> fixture)
    : BrainTest<KernelKindsConfigurator>(fixture)
{
    [Fact]
    public async Task Typed_call_travels_the_universal_envelope()
    {
        var proxy = NeuronProxy.Create<ITestNeuron>(
            Cluster.Client, AddressKey("test", "proxy-1"), OwnerSession);
        var reply = await proxy.EchoAsync(new EchoRequest(7));
        Assert.Equal(7, reply.V);
    }

    [Fact]
    public async Task Grain_failure_surfaces_as_unwrapped_brain_exception()
    {
        var proxy = NeuronProxy.Create<ITestNeuron>(
            Cluster.Client, "owner|actor/test|nope/proxy-err", OwnerSession);
        var exception = await Assert.ThrowsAsync<BrainException>(() => proxy.EchoAsync(new EchoRequest(1)));
        Assert.Equal(BrainErrors.UnknownKind, exception.Code);
    }

    [Fact]
    public async Task Typed_reply_copies_value_and_receipt_metadata()
    {
        var proxy = NeuronProxy.Create<ITestNeuron>(
            Cluster.Client, AddressKey("test", $"proxy-reply-{Guid.NewGuid():N}"), OwnerSession);

        var reply = await proxy.EchoWithReceiptAsync(new EchoRequest(9));

        Assert.Equal(9, reply.Value.V);
        Assert.Equal(1, reply.Revision);
        Assert.Null(reply.EffectKey);
    }
}
