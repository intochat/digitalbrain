using DigitalBrain.Access;
using DigitalBrain.Capabilities;
using DigitalBrain.Neurons;

namespace DigitalBrain.Testing.Tests.Unit.Hosting;

public interface IEdgeProbeNeuron
{
    Task<string> Ping();

    [Internal]
    Task<string> InternalPing();
}

[Neuron("test.edge.probe")]
public sealed class EdgeProbeNeuron : Neuron, IEdgeProbeNeuron
{
    public Task<string> Ping() => Task.FromResult(Context.Caller?.Actor.GetType().Name ?? "none");

    public Task<string> InternalPing() => Task.FromResult(Context.Caller?.Actor.GetType().Name ?? "none");
}

public sealed class EdgeAuthorityFacts
{
    private static readonly BrainId Brain = new("testos-edge");

    [Fact]
    public async Task TestOS_Edge_SupportsKernelAndTypedCallers_WithoutExposingEdgeInNeuronServices()
    {
        await using var os = global::DigitalBrain.Testing.TestOS.Local().Register<EdgeProbeNeuron>();
        os.AccessOptions.UnstampedEdgeIsKernel = false;
        os.AccessOptions.AuditContractInternal = false;

        Assert.Null(os.Services.GetService(typeof(EdgeAuthority)));
        Assert.False(os.Edge.IsKernelActive);

        var id = new NeuronId(Brain, "test.edge.probe", "default");
        var proxy = os.Get<IEdgeProbeNeuron>(id);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => proxy.Ping());

        using (os.Edge.EnterKernel(Brain))
        {
            Assert.True(os.Edge.IsKernelActive);
            Assert.Equal("Kernel", await proxy.InternalPing());
        }

        Assert.False(os.Edge.IsKernelActive);

        using (os.Edge.Enter(os.Edge.Guest("try-1", "try-1", Brain)))
        {
            Assert.False(os.Edge.IsKernelActive);
            Assert.Equal("App", await proxy.Ping());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => proxy.InternalPing());
        }

        using (os.Edge.Enter(os.Edge.RelayedPerson("alice", "acct-1", Brain)))
        {
            Assert.False(os.Edge.IsKernelActive);
            Assert.Equal("Person", await proxy.InternalPing());
        }

        using (os.Edge.Enter(os.Edge.From(CapabilityCallers.Of("alice", "acct-1", Brain.Value))))
        {
            Assert.False(os.Edge.IsKernelActive);
            Assert.Equal("Person", await proxy.Ping());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => proxy.InternalPing());
        }
    }
}
