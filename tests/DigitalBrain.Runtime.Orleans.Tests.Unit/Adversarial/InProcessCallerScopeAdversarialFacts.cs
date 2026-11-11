using DigitalBrain.Modules;
using System.Reflection;
using System.Text.Json;
using DigitalBrain.Access;
using DigitalBrain.Runtime.Orleans.Access;
using Orleans;
using Orleans.Metadata;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Adversarial;

/// <summary>
/// Empirical adversarial verification of InProcessCallerScope security:
/// 1. Token unforgeability: External callers injecting serialized objects, strings, byte arrays,
///    or new object instances into RequestContext under "db.internal.inprocess_scope" cannot activate the scope.
/// 2. Tamper resistance through CallerEdgeFilter: Injected tokens fail to bypass Actor restrictions.
/// 3. AsyncLocal and RequestContext isolation: Zero cross-task or cross-thread leakage under high concurrency.
/// 4. Exception unwinding: Nested scopes restore previous states under fault injection.
/// 5. Direct human provenance preservation vs internal kernel privilege.
/// </summary>
public sealed class InProcessCallerScopeAdversarialFacts
{
    private const string RequestContextKey = "db.internal.inprocess_scope";
    private static readonly GrainId TargetNeuron = GrainId.Create("neuron", "victim-brain/vault/default");
    private static readonly GrainId ExternalSysClient = GrainId.Create("sys.client", "gateway-client-1");

    [Fact]
    public void DefaultState_IsActiveIsFalse()
    {
        RequestContext.Remove(RequestContextKey);
        Assert.False(InProcessCallerScope.IsActive, "InProcessCallerScope must be inactive by default.");
    }

    [Theory]
    [InlineData("db.internal.inprocess_scope")]
    [InlineData("ProcessSecretToken")]
    [InlineData("true")]
    [InlineData("System.Object")]
    public void ExternalTokenInjection_StringPayload_DoesNotActivateScope(string injectedString)
    {
        try
        {
            RequestContext.Set(RequestContextKey, injectedString);
            Assert.False(InProcessCallerScope.IsActive,
                $"Injecting string '{injectedString}' into RequestContext must not activate InProcessCallerScope.");
        }
        finally
        {
            RequestContext.Remove(RequestContextKey);
        }
    }

    [Fact]
    public void ExternalTokenInjection_ArbitraryObjectPayload_DoesNotActivateScope()
    {
        try
        {
            RequestContext.Set(RequestContextKey, new object());
            Assert.False(InProcessCallerScope.IsActive,
                "Injecting a new object() instance must not activate InProcessCallerScope because ReferenceEquals fails.");
        }
        finally
        {
            RequestContext.Remove(RequestContextKey);
        }
    }

    [Fact]
    public void ExternalTokenInjection_DeserializedPayload_DoesNotActivateScope()
    {
        try
        {
            // Simulate receiving a serialized object across the wire and deserializing it
            var json = JsonSerializer.Serialize(new { Secret = "db.internal.inprocess_scope", Nonce = 12345 });
            var deserialized = JsonSerializer.Deserialize<object>(json)!;

            RequestContext.Set(RequestContextKey, deserialized);
            Assert.False(InProcessCallerScope.IsActive,
                "Deserialized object payload must never be reference equal to ProcessSecretToken.");
        }
        finally
        {
            RequestContext.Remove(RequestContextKey);
        }
    }

    [Fact]
    public void ExternalTokenInjection_PrimitiveAndBytePayloads_DoNotActivateScope()
    {
        try
        {
            RequestContext.Set(RequestContextKey, true);
            Assert.False(InProcessCallerScope.IsActive);

            RequestContext.Set(RequestContextKey, 1);
            Assert.False(InProcessCallerScope.IsActive);

            RequestContext.Set(RequestContextKey, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
            Assert.False(InProcessCallerScope.IsActive);
        }
        finally
        {
            RequestContext.Remove(RequestContextKey);
        }
    }

    [Theory]
    [InlineData(CallerWire.ActorKernel)]
    [InlineData(CallerWire.ActorScheduler)]
    [InlineData(CallerWire.ActorModule)]
    public async Task ExternalCaller_WithInjectedToken_AttemptingPrivilegedActor_IsRejectedByCallerEdgeFilter(byte internalActor)
    {
        var filter = new CallerEdgeFilter();
        var wire = new CallerWire
        {
            Actor = internalActor,
            Brain = "victim-brain",
            Inside = true, // Adversary claims Inside
            Via = "victim-brain/proxy/0"
        };

        try
        {
            // Adversary injects fake token into RequestContext
            RequestContext.Set(RequestContextKey, new object());
            RequestContext.Set(OrleansCallerAmbient.Key, wire);

            var context = new MockIncomingContext(TargetNeuron, sourceGrainId: ExternalSysClient);

            // CallerEdgeFilter must throw UnauthorizedAccessException because InProcessCallerScope is NOT active
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => filter.Invoke(context));
        }
        finally
        {
            RequestContext.Remove(RequestContextKey);
            RequestContext.Remove(OrleansCallerAmbient.Key);
        }
    }

    [Theory]
    [InlineData("sys.client")]
    [InlineData("SYS.CLIENT")]
    [InlineData("Sys.Client")]
    [InlineData("sys.client/gateway")]
    [InlineData("sys.client.sub")]
    public async Task SourceId_CaseInsensitivePrefixMatching_CorrectlyIdentifiedAsExternal(string grainType)
    {
        var filter = new CallerEdgeFilter();
        var wire = new CallerWire
        {
            Actor = CallerWire.ActorKernel,
            Brain = "victim-brain"
        };

        try
        {
            RequestContext.Set(OrleansCallerAmbient.Key, wire);
            var context = new MockIncomingContext(TargetNeuron, sourceGrainId: GrainId.Create(grainType, "123"));

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => filter.Invoke(context));
        }
        finally
        {
            RequestContext.Remove(OrleansCallerAmbient.Key);
        }
    }

    [Fact]
    public async Task InProcessCallerScope_LegitimateExecution_GrantsKernelAuthority()
    {
        var filter = new CallerEdgeFilter();
        var wire = new CallerWire
        {
            Actor = CallerWire.ActorKernel,
            Brain = "victim-brain",
            Inside = false
        };

        try
        {
            RequestContext.Set(OrleansCallerAmbient.Key, wire);
            var executed = false;
            var context = new MockIncomingContext(TargetNeuron, sourceGrainId: null, () =>
            {
                executed = true;
                Assert.True(wire.Inside, "InProcessCallerScope must allow internal actor to be marked Inside = true.");
                return Task.CompletedTask;
            });

            using (InProcessCallerScope.Enter())
            {
                Assert.True(InProcessCallerScope.IsActive);
                await filter.Invoke(context);
                Assert.True(executed);
            }

            // Immediately after scope exit, IsActive must be false
            Assert.False(InProcessCallerScope.IsActive);

            // Re-running without scope must throw UnauthorizedAccessException
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => filter.Invoke(context));
        }
        finally
        {
            RequestContext.Remove(OrleansCallerAmbient.Key);
        }
    }

    [Fact]
    public async Task InProcessCallerScope_PreservesDirectHumanProvenance()
    {
        var filter = new CallerEdgeFilter();
        var wire = new CallerWire
        {
            Actor = CallerWire.ActorPerson,
            Principal = "alice",
            Account = "acct-1",
            Brain = "victim-brain",
            Inside = false,
            Via = null
        };

        try
        {
            RequestContext.Set(OrleansCallerAmbient.Key, wire);
            var executed = false;
            var context = new MockIncomingContext(TargetNeuron, sourceGrainId: null, () =>
            {
                executed = true;
                // Important invariant: Person calls inside in-process host must NOT be marked inside,
                // so that Caller.Direct (Via is null && !Inside) remains TRUE for direct approvals!
                Assert.False(wire.Inside, "Human Person caller must preserve Inside = false.");
                Assert.Null(wire.Via);
                return Task.CompletedTask;
            });

            using (InProcessCallerScope.Enter())
            {
                await filter.Invoke(context);
                Assert.True(executed);
            }

            var caller = OrleansCallerAmbient.Instance.Current;
            Assert.NotNull(caller);
            Assert.True(caller.Direct, "Caller.Direct must be true for direct human calls.");
        }
        finally
        {
            RequestContext.Remove(OrleansCallerAmbient.Key);
        }
    }

    [Fact]
    public void NestedScopes_RestorePreviousStatesCorrectly()
    {
        Assert.False(InProcessCallerScope.IsActive);
        Assert.Null(RequestContext.Get(RequestContextKey));

        using (InProcessCallerScope.Enter())
        {
            Assert.True(InProcessCallerScope.IsActive);

            using (InProcessCallerScope.Enter())
            {
                Assert.True(InProcessCallerScope.IsActive);

                using (InProcessCallerScope.Enter())
                {
                    Assert.True(InProcessCallerScope.IsActive);
                }

                Assert.True(InProcessCallerScope.IsActive);
            }

            Assert.True(InProcessCallerScope.IsActive);
        }

        Assert.False(InProcessCallerScope.IsActive);
        Assert.Null(RequestContext.Get(RequestContextKey));
    }

    [Fact]
    public void ExceptionUnwinding_RestoresPreviousStateCleanly()
    {
        Assert.False(InProcessCallerScope.IsActive);

        Action act = () =>
        {
            using var _ = InProcessCallerScope.Enter();
            Assert.True(InProcessCallerScope.IsActive);
            throw new InvalidOperationException("Simulated fault");
        };

        Assert.Throws<InvalidOperationException>(act);

        Assert.False(InProcessCallerScope.IsActive);
        Assert.Null(RequestContext.Get(RequestContextKey));
    }

    [Fact]
    public async Task ConcurrencyStress_NoCrossTaskLeakage()
    {
        const int concurrentTasks = 100;
        const int iterationsPerTask = 20;

        var tasks = Enumerable.Range(0, concurrentTasks).Select(async taskId =>
        {
            var isPrivilegedTask = (taskId % 2) == 0;

            for (var i = 0; i < iterationsPerTask; i++)
            {
                if (isPrivilegedTask)
                {
                    using (InProcessCallerScope.Enter())
                    {
                        Assert.True(InProcessCallerScope.IsActive, $"Task {taskId} iteration {i} must be active inside scope.");
                        await Task.Delay(1).ConfigureAwait(false);
                        Assert.True(InProcessCallerScope.IsActive, $"Task {taskId} iteration {i} must remain active after delay.");
                    }
                    Assert.False(InProcessCallerScope.IsActive, $"Task {taskId} iteration {i} must be inactive after scope exit.");
                }
                else
                {
                    Assert.False(InProcessCallerScope.IsActive, $"Unprivileged task {taskId} iteration {i} must be inactive.");
                    await Task.Delay(1).ConfigureAwait(false);
                    Assert.False(InProcessCallerScope.IsActive, $"Unprivileged task {taskId} iteration {i} must remain inactive after delay.");
                }
            }
        });

        await Task.WhenAll(tasks);
        Assert.False(InProcessCallerScope.IsActive, "After all tasks complete, scope must be inactive.");
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
