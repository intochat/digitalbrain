using Brain.Client;
using Brain.Contracts;
using Xunit;

namespace Brain.KernelTests;

public interface ITestNeuron : INeuronContract
{
    [NeuronContract("test.echo.v1")]
    Task<EchoReply> EchoAsync(EchoRequest request);
}

public sealed record EchoRequest(int V);
public sealed record EchoReply(int V);

public class TypedProxyTests(ClusterFixture fixture) : IClassFixture<ClusterFixture>
{
    [Fact]
    public async Task Typed_call_travels_the_universal_envelope()
    {
        var proxy = NeuronProxy.Create<ITestNeuron>(
            fixture.Cluster.Client, fixture.AddressKey("test", "proxy-1"), fixture.OwnerSession);
        var reply = await proxy.EchoAsync(new EchoRequest(7));
        Assert.Equal(7, reply.V);
    }

    [Fact]
    public async Task Grain_failure_surfaces_as_unwrapped_brain_exception()
    {
        var proxy = NeuronProxy.Create<ITestNeuron>(
            fixture.Cluster.Client, "owner|actor/test|nope/proxy-err", fixture.OwnerSession);
        var exception = await Assert.ThrowsAsync<BrainException>(() => proxy.EchoAsync(new EchoRequest(1)));
        Assert.Equal(BrainErrors.UnknownKind, exception.Code);
    }
}
