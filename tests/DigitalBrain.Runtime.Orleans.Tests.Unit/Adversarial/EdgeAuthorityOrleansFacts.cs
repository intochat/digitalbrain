using System.Reflection;
using DigitalBrain.Access;
using DigitalBrain.Capabilities;
using DigitalBrain.Runtime.Orleans.Access;
using DigitalBrain.Runtime.Orleans.Capabilities;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.Metadata;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Adversarial;

public sealed class EdgeAuthorityOrleansFacts
{
    private static readonly BrainId VictimBrain = new("victim-brain");
    private static readonly GrainId TargetNeuron = GrainId.Create("neuron", "victim-brain/vault/default");
    private static readonly GrainId ExternalSysClient = GrainId.Create("sys.client", "client-1");

    [Fact]
    public void AddDigitalBrainRuntime_RegistersEdgeAuthorityBackedByOrleansCallerAmbient()
    {
        var services = new ServiceCollection();
        var silo = new TestSiloBuilder(services);

        silo.AddDigitalBrainRuntime();

        using var provider = services.BuildServiceProvider();
        var edge = provider.GetService<EdgeAuthority>();
        var ambient = provider.GetService<ICallerAmbient>();

        Assert.NotNull(edge);
        Assert.Same(OrleansCallerAmbient.Instance, ambient);

        using (edge.EnterKernel(VictimBrain))
        {
            Assert.True(edge.IsKernelActive);
            Assert.IsType<Actor.Kernel>(ambient!.Current?.Actor);
            Assert.Equal(VictimBrain, ambient.Current!.Brain);
        }

        Assert.False(edge.IsKernelActive);
        Assert.Null(ambient!.Current);
    }

    [Fact]
    public async Task EnterKernel_SatisfiesCallerEdgeFilter_WithoutManualInProcessCallerScope_AndRevertsOnDispose()
    {
        using var provider = CreateRuntimeServices();
        var edge = provider.GetRequiredService<EdgeAuthority>();
        var filter = new CallerEdgeFilter();

        Assert.False(edge.IsKernelActive);
        Assert.False(InProcessCallerScope.IsActive);

        var executed = false;
        using (edge.EnterKernel(VictimBrain))
        {
            Assert.True(edge.IsKernelActive);
            Assert.True(InProcessCallerScope.IsActive);

            var context = new MockIncomingContext(TargetNeuron, sourceGrainId: ExternalSysClient, () =>
            {
                executed = true;
                var wire = Assert.IsType<CallerWire>(RequestContext.Get(OrleansCallerAmbient.Key));
                Assert.Equal(CallerWire.ActorKernel, wire.Actor);
                Assert.True(wire.Inside);
                return Task.CompletedTask;
            });

            await filter.Invoke(context);
            Assert.True(executed);
        }

        Assert.False(edge.IsKernelActive);
        Assert.False(InProcessCallerScope.IsActive);
        Assert.Null(OrleansCallerAmbient.Instance.Current);

        // Immediately after scope exit, an external wire claiming Kernel must fail closed
        RequestContext.Set(OrleansCallerAmbient.Key, new CallerWire
        {
            Actor = CallerWire.ActorKernel,
            Brain = VictimBrain.Value,
            Inside = true,
        });
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                filter.Invoke(new MockIncomingContext(TargetNeuron, sourceGrainId: null)));
        }
        finally
        {
            RequestContext.Remove(OrleansCallerAmbient.Key);
        }
    }

    [Fact]
    public async Task EdgeAuthority_ModuleAndScheduler_SatisfyCallerEdgeFilter_WithoutGrantingKernelAmbient()
    {
        using var provider = CreateRuntimeServices();
        var edge = provider.GetRequiredService<EdgeAuthority>();
        var filter = new CallerEdgeFilter();

        using (edge.Enter(edge.Module("auth-mod", VictimBrain)))
        {
            Assert.False(edge.IsKernelActive);
            var executed = false;
            await filter.Invoke(new MockIncomingContext(TargetNeuron, sourceGrainId: null, () =>
            {
                executed = true;
                var wire = Assert.IsType<CallerWire>(RequestContext.Get(OrleansCallerAmbient.Key));
                Assert.Equal(CallerWire.ActorModule, wire.Actor);
                Assert.True(wire.Inside);
                return Task.CompletedTask;
            }));
            Assert.True(executed);
        }

        using (edge.Enter(edge.Scheduler(VictimBrain)))
        {
            Assert.False(edge.IsKernelActive);
            var executed = false;
            await filter.Invoke(new MockIncomingContext(TargetNeuron, sourceGrainId: ExternalSysClient, () =>
            {
                executed = true;
                var wire = Assert.IsType<CallerWire>(RequestContext.Get(OrleansCallerAmbient.Key));
                Assert.Equal(CallerWire.ActorScheduler, wire.Actor);
                Assert.True(wire.Inside);
                return Task.CompletedTask;
            }));
            Assert.True(executed);
        }
    }

    [Fact]
    public async Task EdgeAuthority_RelayedPerson_PreservesProvenanceInCallerEdgeFilter_AndCannotEscalateToKernel()
    {
        using var provider = CreateRuntimeServices();
        var edge = provider.GetRequiredService<EdgeAuthority>();
        var filter = new CallerEdgeFilter();
        var via = new NeuronId(VictimBrain, "apps.flows", "default");

        using (edge.Enter(edge.RelayedPerson("alice", "acct-1", VictimBrain, via: via, conversation: "conv-1", inside: true)))
        {
            Assert.False(edge.IsKernelActive);
            Assert.False(InProcessCallerScope.IsActive);

            var executed = false;
            await filter.Invoke(new MockIncomingContext(TargetNeuron, sourceGrainId: ExternalSysClient, () =>
            {
                executed = true;
                var current = OrleansCallerAmbient.Instance.Current;
                Assert.NotNull(current);
                Assert.Equal(new Actor.Person("alice", "acct-1"), current.Actor);
                Assert.Equal(via, current.Via);
                Assert.True(current.Inside);
                Assert.False(current.Direct);
                return Task.CompletedTask;
            }));
            Assert.True(executed);

            // Even inside a RelayedPerson scope, forging Kernel in RequestContext must fail closed
            var previous = RequestContext.Get(OrleansCallerAmbient.Key);
            RequestContext.Set(OrleansCallerAmbient.Key, new CallerWire
            {
                Actor = CallerWire.ActorKernel,
                Brain = VictimBrain.Value,
                Inside = true,
            });
            try
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    filter.Invoke(new MockIncomingContext(TargetNeuron, sourceGrainId: ExternalSysClient)));
            }
            finally
            {
                if (previous is null) RequestContext.Remove(OrleansCallerAmbient.Key);
                else RequestContext.Set(OrleansCallerAmbient.Key, previous);
            }
        }

        Assert.False(InProcessCallerScope.IsEdgeActive);
    }

    [Fact]
    public async Task EdgeAuthority_PersonAndGuest_PreserveDirectSemantics_AndCannotEscalateToKernel()
    {
        using var provider = CreateRuntimeServices();
        var edge = provider.GetRequiredService<EdgeAuthority>();
        var filter = new CallerEdgeFilter();

        using (edge.Enter(edge.Person("alice", "acct-1", VictimBrain)))
        {
            Assert.False(edge.IsKernelActive);
            Assert.False(InProcessCallerScope.IsActive);

            await filter.Invoke(new MockIncomingContext(TargetNeuron, sourceGrainId: null, () =>
            {
                var current = OrleansCallerAmbient.Instance.Current;
                Assert.NotNull(current);
                Assert.True(current.Direct);
                Assert.False(current.Inside);
                Assert.Null(current.Via);
                return Task.CompletedTask;
            }));

            var previous = RequestContext.Get(OrleansCallerAmbient.Key);
            RequestContext.Set(OrleansCallerAmbient.Key, new CallerWire
            {
                Actor = CallerWire.ActorKernel,
                Brain = VictimBrain.Value,
                Inside = true,
            });
            try
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    filter.Invoke(new MockIncomingContext(TargetNeuron, sourceGrainId: null)));
            }
            finally
            {
                if (previous is null) RequestContext.Remove(OrleansCallerAmbient.Key);
                else RequestContext.Set(OrleansCallerAmbient.Key, previous);
            }
        }

        using (edge.Enter(edge.Guest("try-42", "try-42", VictimBrain)))
        {
            Assert.False(edge.IsKernelActive);
            var current = OrleansCallerAmbient.Instance.Current;
            Assert.Equal(new Actor.App("guest", new Actor.Person("try-42", "try-42")), current?.Actor);
        }
    }

    [Fact]
    public void ClearCaller_ClearsAmbientAndCapabilityKeys_AndRestoresBothOnDispose()
    {
        using var provider = CreateRuntimeServices();
        var edge = provider.GetRequiredService<EdgeAuthority>();
        var person = CapabilityCallers.Of("alice", "acct-1", VictimBrain.Value, CallerKind.App, appId: "notes", conversationId: "c1");

        using (RuntimeCallerContext.Stamp(person))
        {
            Assert.NotNull(edge.Current);
            Assert.NotNull(RuntimeCallerContext.Current);
            Assert.False(edge.IsKernelActive);

            using (edge.ClearCaller())
            {
                Assert.Null(edge.Current);
                Assert.Null(RuntimeCallerContext.Current);
                Assert.True(edge.IsKernelActive);
            }

            Assert.Equal(new Actor.App("notes", new Actor.Person("alice", "acct-1")), edge.Current?.Actor);
            Assert.Equal(person, RuntimeCallerContext.Current);
            Assert.False(edge.IsKernelActive);
        }

        Assert.Null(edge.Current);
        Assert.Null(RuntimeCallerContext.Current);
        Assert.False(edge.IsKernelActive);
    }

    [Fact]
    public void EnterInProcess_DoesNotElevateNamedNonKernelCaller_AndEnablesUnstampedKernelCheck()
    {
        using var provider = CreateRuntimeServices();
        var edge = provider.GetRequiredService<EdgeAuthority>();

        using (edge.EnterInProcess())
        {
            Assert.Null(edge.Current);
            Assert.True(edge.IsKernelActive);

            using (edge.Enter(edge.Person("alice", "acct-1", VictimBrain)))
            {
                Assert.False(edge.IsKernelActive);
                Assert.True(InProcessCallerScope.IsActive);
            }

            Assert.Null(edge.Current);
            Assert.True(edge.IsKernelActive);
        }

        Assert.False(edge.IsKernelActive);
    }

    private static ServiceProvider CreateRuntimeServices()
    {
        var services = new ServiceCollection();
        new TestSiloBuilder(services).AddDigitalBrainRuntime();
        return services.BuildServiceProvider();
    }

    private sealed class TestSiloBuilder(IServiceCollection services) : ISiloBuilder
    {
        public IServiceCollection Services => services;
        public Microsoft.Extensions.Configuration.IConfiguration Configuration { get; } =
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
    }

    private sealed class MockIncomingContext(
        GrainId targetGrainId,
        GrainId? sourceGrainId = null,
        Func<Task>? onInvoke = null) : IIncomingGrainCallContext
    {
        public GrainId TargetId => targetGrainId;
        public GrainId? SourceId => sourceGrainId;
        public Task Invoke() => onInvoke is not null ? onInvoke() : Task.CompletedTask;

        IGrainContext IIncomingGrainCallContext.TargetContext => null!;
        IInvokable IGrainCallContext.Request => null!;
        Response? IGrainCallContext.Response { get; set; }
        object? IGrainCallContext.Result { get; set; }
        object IGrainCallContext.Grain => null!;
        MethodInfo IGrainCallContext.InterfaceMethod => null!;
        MethodInfo IIncomingGrainCallContext.ImplementationMethod => null!;
        GrainInterfaceType IGrainCallContext.InterfaceType => default;
        string IGrainCallContext.InterfaceName => string.Empty;
        string IGrainCallContext.MethodName => string.Empty;
    }
}
