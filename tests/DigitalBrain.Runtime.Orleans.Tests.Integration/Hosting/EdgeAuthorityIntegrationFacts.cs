using DigitalBrain.Access;
using DigitalBrain.Contracts;
using DigitalBrain.Hosting;
using DigitalBrain.Invocation;
using DigitalBrain.Modules;
using DigitalBrain.Neurons;
using DigitalBrain.Runtime.Orleans.Access;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;

[DefaultNeuronType("test.edge-authority")]
public interface IEdgeAuthorityProbe
{
    Task<string> Describe();

    [Internal]
    Task<string> DescribeInternal();
}

[Neuron("test.edge-authority")]
public sealed class EdgeAuthorityProbeNeuron : Neuron, IEdgeAuthorityProbe
{
    public Task<string> Describe() => Task.FromResult(Format(Context.Caller));

    public Task<string> DescribeInternal() => Task.FromResult(Format(Context.Caller));

    private static string Format(Caller? caller) => caller is null
        ? "null"
        : $"{caller.Actor.GetType().Name}:{caller.Brain.Value}:direct={caller.Direct}:inside={caller.Inside}:via={caller.Via?.ToString() ?? "none"}";
}

[Module("test-edge-authority-module")]
public sealed class TestEdgeAuthorityModule : Module
{
    public static ModuleManifest Manifest { get; } = new(
        Provides: [],
        Requires: [],
        Needs: [],
        Settings: [],
        Secrets: []);
}

public sealed class EdgeAuthorityIntegrationFacts(EdgeAuthorityIntegrationFacts.Fixture fixture)
    : IClassFixture<EdgeAuthorityIntegrationFacts.Fixture>
{
    public sealed class Fixture : IAsyncLifetime
    {
        private IInProcessHost? _host;
        private int _counter;

        public IServiceProvider Services => (_host ?? throw new InvalidOperationException("Silo host is not started.")).Services;

        public BrainId NewBrain(string prefix) => new($"{prefix}-{Interlocked.Increment(ref _counter)}");

        public async ValueTask InitializeAsync()
        {
            var factory = new LocalSiloHostFactory(
                new BrainStorageRegistration(typeof(Fixture).Assembly),
                services =>
                {
                    services.AddSingleton(new AccessOptions { UnstampedEdgeIsKernel = false, AuditContractInternal = false });
                });

            _host = await factory.Start(
                new InProcessHostOptions
                {
                    Modules = [typeof(TestEdgeAuthorityModule)],
                },
                CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            if (_host is not null)
            {
                await _host.DisposeAsync();
            }
        }
    }

    [Fact]
    public void AddDigitalBrainRuntime_ResolvesEdgeAuthority_InSiloServices()
    {
        var edge = fixture.Services.GetService<EdgeAuthority>();
        Assert.NotNull(edge);
        Assert.False(edge.IsKernelActive);
    }

    [Fact]
    public async Task EnterKernel_SatisfiesCallerEdgeFilter_EvenViaDirectGrainFactoryCall()
    {
        var edge = fixture.Services.GetRequiredService<EdgeAuthority>();
        var grains = fixture.Services.GetRequiredService<IGrainFactory>();
        var brain = fixture.NewBrain("edge-kernel");
        var id = new NeuronId(brain, "test.edge-authority", "default");
        var host = grains.GetGrain<INeuronHost>(GrainId.Create("neuron.host", id.ToString()));
        var call = new RuntimeInvocation("", NeuronProxy.ContractName(typeof(IEdgeAuthorityProbe)), id.ToString(), nameof(IEdgeAuthorityProbe.DescribeInternal), []);

        // Bypass OrleansInvocationSink intentionally to prove EnterKernel itself satisfies CallerEdgeFilter.
        using (edge.EnterKernel(brain))
        {
            Assert.True(edge.IsKernelActive);
            var json = await host.Invoke(call, TestContext.Current.CancellationToken);
            Assert.Equal($"Kernel:{brain.Value}:direct=False:inside=True:via=none", json!.Value.GetString());
        }

        Assert.False(edge.IsKernelActive);

        // Without EnterKernel, a forged RequestContext Kernel wire must fail at CallerEdgeFilter.
        RequestContext.Set(OrleansCallerAmbient.Key, new CallerWire
        {
            Actor = CallerWire.ActorKernel,
            Brain = brain.Value,
            Inside = true,
        });
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => host.Invoke(call, TestContext.Current.CancellationToken));
        }
        finally
        {
            RequestContext.Remove(OrleansCallerAmbient.Key);
        }
    }

    [Fact]
    public async Task EdgeAuthority_VerifiedCallers_EnforceBoundariesEndToEnd()
    {
        var edge = fixture.Services.GetRequiredService<EdgeAuthority>();
        var brain = fixture.NewBrain("edge-bound");
        var otherBrain = fixture.NewBrain("edge-other");
        var id = new NeuronId(brain, "test.edge-authority", "default");
        var probe = SiloNeurons.Get<IEdgeAuthorityProbe>(fixture.Services, id);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => probe.Describe());

        using (edge.Enter(edge.Person("alice", "acct-1", brain)))
        {
            Assert.False(edge.IsKernelActive);
            Assert.Equal($"Person:{brain.Value}:direct=True:inside=False:via=none", await probe.Describe());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => probe.DescribeInternal());
        }

        using (edge.Enter(edge.Person("bob", "acct-2", otherBrain)))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => probe.Describe());
        }

        using (edge.Enter(edge.Guest("try-9", "try-9", brain)))
        {
            Assert.Equal($"App:{brain.Value}:direct=True:inside=False:via=none", await probe.Describe());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => probe.DescribeInternal());
        }

        var relay = new NeuronId(brain, "apps.flows", "default");
        using (edge.Enter(edge.RelayedPerson("alice", "acct-1", brain, via: relay, inside: true)))
        {
            Assert.Equal($"Person:{brain.Value}:direct=False:inside=True:via={relay}", await probe.DescribeInternal());
        }

        using (edge.Enter(edge.Module("auth-mod", brain)))
        {
            Assert.Equal($"Module:{brain.Value}:direct=False:inside=True:via=none", await probe.DescribeInternal());
        }
    }
}
