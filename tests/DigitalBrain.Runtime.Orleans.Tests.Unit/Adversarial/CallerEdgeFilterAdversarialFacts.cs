using DigitalBrain.Modules;
using System.Reflection;
using DigitalBrain.Access;
using DigitalBrain.Runtime.Orleans.Access;
using Orleans;
using Orleans.Metadata;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Adversarial;

/// <summary>
/// Empirical adversarial verification of CallerEdgeFilter tamper resistance:
/// 1. External callers (SourceId == null or SourceId starting with sys.client) cannot forge Inside = true.
/// 2. External callers cannot forge Via != null.
/// 3. Internal grain calls (SourceId != null and not sys.client) are stamped Inside = true and preserve Via.
/// 4. Corrupted / non-CallerWire payloads in RequestContext fail closed safely without crashing the pipeline.
/// </summary>
public sealed class CallerEdgeFilterAdversarialFacts
{
    private static readonly GrainId TargetNeuron = GrainId.Create("neuron", "victim-brain/vault/default");
    private static readonly GrainId InternalGrain = GrainId.Create("neuron", "victim-brain/worker/0");
    private static readonly GrainId ExternalClientGrain = GrainId.Create("sys.client", "client-connection-99");

    [Fact]
    public async Task ExternalCaller_NullSourceId_CannotForgeInsideOrVia()
    {
        var filter = new CallerEdgeFilter();
        var wire = new CallerWire
        {
            Actor = 0, // Person
            Principal = "malicious-user",
            Account = "acct-666",
            Brain = "victim-brain",
            Inside = true, // Adversarial attempt to forge Inside = true
            Via = "victim-brain/trusted-service/0" // Adversarial attempt to forge Via
        };

        RequestContext.Set(OrleansCallerAmbient.Key, wire);
        try
        {
            var executed = false;
            var context = new MockIncomingContext(TargetNeuron, sourceGrainId: null, () =>
            {
                executed = true;
                // Verify inside invocation context: wire has been sanitized
                Assert.False(wire.Inside, "CallerEdgeFilter must reset Inside to false for external clients.");
                Assert.Null(wire.Via);
                return Task.CompletedTask;
            });

            await filter.Invoke(context);
            Assert.True(executed);

            // Verify the ambient read reflects sanitized values
            var ambientCaller = OrleansCallerAmbient.Instance.Current;
            Assert.NotNull(ambientCaller);
            Assert.False(ambientCaller.Inside, "Ambient Caller.Inside must be false.");
            Assert.Null(ambientCaller.Via);
            Assert.True(ambientCaller.Direct, "Sanitized direct human caller must evaluate Direct == true.");
        }
        finally
        {
            RequestContext.Remove(OrleansCallerAmbient.Key);
        }
    }

    [Fact]
    public async Task ExternalCaller_SysClientGrain_CannotForgeInsideOrVia()
    {
        var filter = new CallerEdgeFilter();
        var wire = new CallerWire
        {
            Actor = 1, // Agent
            Principal = "evil-agent",
            Account = "acct-99",
            Brain = "victim-brain",
            Inside = true,
            Via = "victim-brain/proxy/0"
        };

        RequestContext.Set(OrleansCallerAmbient.Key, wire);
        try
        {
            var executed = false;
            var context = new MockIncomingContext(TargetNeuron, sourceGrainId: ExternalClientGrain, () =>
            {
                executed = true;
                Assert.False(wire.Inside, "sys.client grain must be treated as external; Inside must be false.");
                Assert.Null(wire.Via);
                return Task.CompletedTask;
            });

            await filter.Invoke(context);
            Assert.True(executed);

            var ambientCaller = OrleansCallerAmbient.Instance.Current;
            Assert.NotNull(ambientCaller);
            Assert.False(ambientCaller.Inside);
            Assert.Null(ambientCaller.Via);
        }
        finally
        {
            RequestContext.Remove(OrleansCallerAmbient.Key);
        }
    }

    [Fact]
    public async Task InternalGrainCaller_StampedInsideTrue_AndPreservesVia()
    {
        var filter = new CallerEdgeFilter();
        var wire = new CallerWire
        {
            Actor = 3, // Module
            Name = "internal-service",
            Brain = "victim-brain",
            Inside = false, // Internal caller may arrive unstamped or with Inside=false
            Via = "victim-brain/dispatcher/0"
        };

        RequestContext.Set(OrleansCallerAmbient.Key, wire);
        try
        {
            var executed = false;
            var context = new MockIncomingContext(TargetNeuron, sourceGrainId: InternalGrain, () =>
            {
                executed = true;
                Assert.True(wire.Inside, "Internal cluster grain call must be stamped with Inside = true.");
                Assert.Equal("victim-brain/dispatcher/0", wire.Via);
                return Task.CompletedTask;
            });

            await filter.Invoke(context);
            Assert.True(executed);

            var ambientCaller = OrleansCallerAmbient.Instance.Current;
            Assert.NotNull(ambientCaller);
            Assert.True(ambientCaller.Inside);
            Assert.Equal("victim-brain/dispatcher/0", ambientCaller.Via?.ToString());
            Assert.False(ambientCaller.Direct, "Internal hopped call must have Direct == false.");
        }
        finally
        {
            RequestContext.Remove(OrleansCallerAmbient.Key);
        }
    }

    [Fact]
    public async Task CorruptedRequestContextPayload_FailsClosedSafely()
    {
        var filter = new CallerEdgeFilter();

        // Adversary sets arbitrary non-CallerWire object in RequestContext
        RequestContext.Set(OrleansCallerAmbient.Key, "bogus-untyped-payload");
        try
        {
            var executed = false;
            var context = new MockIncomingContext(TargetNeuron, sourceGrainId: null, () =>
            {
                executed = true;
                return Task.CompletedTask;
            });

            // Filter must not throw InvalidCastException or crash
            await filter.Invoke(context);
            Assert.True(executed);

            // Ambient reader safely evaluates to null (no valid caller)
            Assert.Null(OrleansCallerAmbient.Instance.Current);
        }
        finally
        {
            RequestContext.Remove(OrleansCallerAmbient.Key);
        }
    }

    [Fact]
    public async Task EmptyRequestContext_ExecutesWithoutException()
    {
        var filter = new CallerEdgeFilter();
        RequestContext.Remove(OrleansCallerAmbient.Key);

        var executed = false;
        var context = new MockIncomingContext(TargetNeuron, sourceGrainId: null, () =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        await filter.Invoke(context);
        Assert.True(executed);
        Assert.Null(OrleansCallerAmbient.Instance.Current);
    }

    [Fact]
    public async Task ExternalCaller_NullSourceId_AttemptingToForgeKernel_ThrowsUnauthorizedAccessException()
    {
        var filter = new CallerEdgeFilter();
        var wire = new CallerWire
        {
            Actor = CallerWire.ActorKernel,
            Brain = "victim-brain",
            Inside = false
        };

        RequestContext.Set(OrleansCallerAmbient.Key, wire);
        try
        {
            var executed = false;
            var context = new MockIncomingContext(TargetNeuron, sourceGrainId: null, () =>
            {
                executed = true;
                return Task.CompletedTask;
            });

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => filter.Invoke(context));
            Assert.False(executed, "Pipeline must not proceed when external caller forges Kernel.");
        }
        finally
        {
            RequestContext.Remove(OrleansCallerAmbient.Key);
        }
    }

    [Fact]
    public async Task ExternalCaller_SysClientGrain_AttemptingToForgeKernel_ThrowsUnauthorizedAccessException()
    {
        var filter = new CallerEdgeFilter();
        var wire = new CallerWire
        {
            Actor = CallerWire.ActorKernel,
            Brain = "victim-brain",
            Inside = false
        };

        RequestContext.Set(OrleansCallerAmbient.Key, wire);
        try
        {
            var executed = false;
            var context = new MockIncomingContext(TargetNeuron, sourceGrainId: ExternalClientGrain, () =>
            {
                executed = true;
                return Task.CompletedTask;
            });

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => filter.Invoke(context));
            Assert.False(executed, "Pipeline must not proceed when sys.client caller forges Kernel.");
        }
        finally
        {
            RequestContext.Remove(OrleansCallerAmbient.Key);
        }
    }

    [Theory]
    [InlineData(CallerWire.ActorModule)]
    [InlineData(CallerWire.ActorScheduler)]
    public async Task ExternalCaller_AttemptingToForgeModuleOrScheduler_ThrowsUnauthorizedAccessException(byte internalActor)
    {
        var filter = new CallerEdgeFilter();
        var wire = new CallerWire
        {
            Actor = internalActor,
            Brain = "victim-brain",
            Name = "forged-name",
            Inside = false
        };

        RequestContext.Set(OrleansCallerAmbient.Key, wire);
        try
        {
            var executed = false;
            var context = new MockIncomingContext(TargetNeuron, sourceGrainId: null, () =>
            {
                executed = true;
                return Task.CompletedTask;
            });

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => filter.Invoke(context));
            Assert.False(executed, "Pipeline must not proceed when external caller forges internal actors.");
        }
        finally
        {
            RequestContext.Remove(OrleansCallerAmbient.Key);
        }
    }

    [Theory]
    [InlineData(6)]
    [InlineData(255)]
    public async Task ExternalCaller_InvalidActorKind_ThrowsUnauthorizedAccessException(byte invalidActor)
    {
        var filter = new CallerEdgeFilter();
        var wire = new CallerWire
        {
            Actor = invalidActor,
            Brain = "victim-brain",
            Inside = false
        };

        RequestContext.Set(OrleansCallerAmbient.Key, wire);
        try
        {
            var executed = false;
            var context = new MockIncomingContext(TargetNeuron, sourceGrainId: null, () =>
            {
                executed = true;
                return Task.CompletedTask;
            });

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => filter.Invoke(context));
            Assert.False(executed, "Pipeline must not proceed when caller presents invalid actor kind.");
        }
        finally
        {
            RequestContext.Remove(OrleansCallerAmbient.Key);
        }
    }

    [Theory]
    [InlineData(CallerWire.ActorModule)]
    [InlineData(CallerWire.ActorScheduler)]
    [InlineData(CallerWire.ActorKernel)]
    public async Task InternalGrainCaller_CanPresentInternalActors_AndStampedInsideTrue(byte internalActor)
    {
        var filter = new CallerEdgeFilter();
        var wire = new CallerWire
        {
            Actor = internalActor,
            Name = "internal-service",
            Brain = "victim-brain",
            Inside = false
        };

        RequestContext.Set(OrleansCallerAmbient.Key, wire);
        try
        {
            var executed = false;
            var context = new MockIncomingContext(TargetNeuron, sourceGrainId: InternalGrain, () =>
            {
                executed = true;
                Assert.True(wire.Inside, "Internal cluster grain call must be stamped with Inside = true.");
                return Task.CompletedTask;
            });

            await filter.Invoke(context);
            Assert.True(executed);

            var ambientCaller = OrleansCallerAmbient.Instance.Current;
            Assert.NotNull(ambientCaller);
            Assert.True(ambientCaller.Inside);
        }
        finally
        {
            RequestContext.Remove(OrleansCallerAmbient.Key);
        }
    }

    [Theory]
    [InlineData(CallerWire.ActorPerson)]
    [InlineData(CallerWire.ActorAgent)]
    [InlineData(CallerWire.ActorApp)]
    public async Task ExternalCaller_LegitimateActors_SucceedsWithProvenanceStripped(byte legitimateActor)
    {
        var filter = new CallerEdgeFilter();
        var wire = new CallerWire
        {
            Actor = legitimateActor,
            Principal = "legit-user",
            Account = "acct-1",
            Name = "app-key",
            Brain = "victim-brain",
            Inside = true, // Attempting to claim inside
            Via = "victim-brain/service/0" // Attempting to claim via
        };

        RequestContext.Set(OrleansCallerAmbient.Key, wire);
        try
        {
            var executed = false;
            var context = new MockIncomingContext(TargetNeuron, sourceGrainId: null, () =>
            {
                executed = true;
                Assert.False(wire.Inside, "Inside must be stripped for external caller.");
                Assert.Null(wire.Via);
                return Task.CompletedTask;
            });

            await filter.Invoke(context);
            Assert.True(executed);

            var ambientCaller = OrleansCallerAmbient.Instance.Current;
            Assert.NotNull(ambientCaller);
            Assert.False(ambientCaller.Inside);
            Assert.Null(ambientCaller.Via);
        }
        finally
        {
            RequestContext.Remove(OrleansCallerAmbient.Key);
        }
    }

    // =========================================================================
    // Mock Context for Grain Call Filter
    // =========================================================================

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
