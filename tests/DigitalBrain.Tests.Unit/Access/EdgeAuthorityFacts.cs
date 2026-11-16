using System.Reflection;
using System.Runtime.CompilerServices;
using DigitalBrain.Access;
using DigitalBrain.Capabilities;
using DigitalBrain.Confirmation;

namespace DigitalBrain.Tests.Unit.Access;

public sealed class EdgeAuthorityFacts
{
    private static readonly BrainId AliceBrain = new("alice-brain");
    private static readonly BrainId BobBrain = new("bob-brain");
    private static readonly NeuronId AliceVault = new(AliceBrain, "vault", "default");
    private static readonly NeuronId RelayNeuron = new(AliceBrain, "apps.flows", "default");

    [Fact]
    public void EdgeAuthority_CannotBeInstantiatedOrAccessedStatically_WithoutInjection()
    {
        Assert.Empty(typeof(EdgeAuthority).GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        var publicStaticMembers = typeof(EdgeAuthority)
            .GetMembers(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m is MethodInfo or PropertyInfo or FieldInfo);
        Assert.Empty(publicStaticMembers);

        var uninitialized = (EdgeAuthority)RuntimeHelpers.GetUninitializedObject(typeof(EdgeAuthority));
        Assert.ThrowsAny<Exception>(() => _ = uninitialized.IsKernelActive);
        Assert.ThrowsAny<Exception>(() => _ = uninitialized.Current);
        Assert.ThrowsAny<Exception>(() => uninitialized.EnterKernel(AliceBrain));
        Assert.ThrowsAny<Exception>(() => uninitialized.EnterInProcess());
        Assert.ThrowsAny<Exception>(() => uninitialized.ClearCaller());
        Assert.ThrowsAny<Exception>(() => uninitialized.Enter(EdgeAuthority.CreateInternal().Person("p", "a", AliceBrain)));
    }

    [Fact]
    public void Guest_MintsRestrictedAppCaller_WithoutDirectPersonAuthority()
    {
        var edge = EdgeAuthority.CreateInternal();

        var guest = edge.Guest("try-123", "try-123", AliceBrain, "conv-1");

        var app = Assert.IsType<Actor.App>(guest.Actor);
        Assert.Equal("guest", app.Key);
        Assert.Equal(new Actor.Person("try-123", "try-123"), app.For);
        Assert.Equal(app.For, guest.For);
        Assert.Equal(AliceBrain, guest.Brain);
        Assert.Equal("conv-1", guest.Conversation);
        Assert.False(guest.Inside);
        Assert.Null(guest.Via);

        Assert.False(Answerer.Of(guest, AliceBrain).IsPerson);
        Assert.True(AccessPolicy.Decide(guest, AliceVault, OperationAccess.Owner).Allowed);
        Assert.Equal(AccessDenial.InternalOnly, AccessPolicy.Decide(guest, AliceVault, OperationAccess.Internal).Denial);
        Assert.Equal(AccessDenial.OutsideBrain, AccessPolicy.Decide(guest, new NeuronId(BobBrain, "vault", "default"), OperationAccess.Owner).Denial);
    }

    [Fact]
    public void RelayedPerson_MintsNonDirectPerson_AndRejectsDirectCombination()
    {
        var edge = EdgeAuthority.CreateInternal();

        var relayedInside = edge.RelayedPerson("alice", "acct-1", AliceBrain);
        Assert.IsType<Actor.Person>(relayedInside.Actor);
        Assert.True(relayedInside.Inside);
        Assert.Null(relayedInside.Via);
        Assert.False(relayedInside.Direct);
        Assert.False(Answerer.Of(relayedInside, AliceBrain).IsPerson);

        var relayedVia = edge.RelayedPerson("alice", "acct-1", AliceBrain, via: RelayNeuron, conversation: "conv-7", inside: false);
        Assert.IsType<Actor.Person>(relayedVia.Actor);
        Assert.False(relayedVia.Inside);
        Assert.Equal(RelayNeuron, relayedVia.Via);
        Assert.Equal("conv-7", relayedVia.Conversation);
        Assert.False(relayedVia.Direct);
        Assert.False(Answerer.Of(relayedVia, AliceBrain).IsPerson);

        Assert.Throws<ArgumentException>(() => edge.RelayedPerson("alice", "acct-1", AliceBrain, via: null, inside: false));
    }

    [Fact]
    public void OnBehalfOf_NeverMintsDirectHumanAuthority()
    {
        var edge = EdgeAuthority.CreateInternal();
        var forPerson = new Actor.Person("alice", "acct-1");

        var delegatedPerson = edge.OnBehalfOf(forPerson, AliceBrain);
        Assert.Equal(forPerson, delegatedPerson.Actor);
        Assert.Equal(forPerson, delegatedPerson.For);
        Assert.True(delegatedPerson.Inside);
        Assert.False(delegatedPerson.Direct);
        Assert.False(Answerer.Of(delegatedPerson, AliceBrain).IsPerson);

        var delegatedApp = edge.OnBehalfOf(forPerson, AliceBrain, viaApp: "assistant-proxy", via: RelayNeuron, conversation: "conv-2");
        var appActor = Assert.IsType<Actor.App>(delegatedApp.Actor);
        Assert.Equal("assistant-proxy", appActor.Key);
        Assert.Equal(forPerson, appActor.For);
        Assert.Equal(RelayNeuron, delegatedApp.Via);
        Assert.True(delegatedApp.Inside);
        Assert.False(delegatedApp.Direct);
        Assert.False(Answerer.Of(delegatedApp, AliceBrain).IsPerson);
    }

    [Fact]
    public void FromCapabilityCaller_MapsAllHostCallerKindsWithoutReflection()
    {
        var edge = EdgeAuthority.CreateInternal();

        var user = edge.From(new CapabilityCaller("alice", "acct-1", AliceBrain.Value, ConversationId: "c1"));
        Assert.Equal(new Actor.Person("alice", "acct-1"), user.Actor);
        Assert.True(user.Direct);
        Assert.Equal("c1", user.Conversation);

        var onBehalf = edge.From(new CapabilityCaller("alice", "acct-1", AliceBrain.Value) { OnBehalfOf = "bob" });
        Assert.Equal(new Actor.Person("alice", "acct-1"), onBehalf.Actor);
        Assert.True(onBehalf.Inside);
        Assert.False(onBehalf.Direct);

        var agent = edge.From(CapabilityCallers.Of("alice", "acct-1", AliceBrain.Value, CallerKind.Agent, conversationId: "c2"));
        Assert.Equal(new Actor.Agent(new Actor.Person("alice", "acct-1")), agent.Actor);
        Assert.False(agent.Inside);
        Assert.Equal("c2", agent.Conversation);

        var app = edge.From(CapabilityCallers.Of("alice", "acct-1", AliceBrain.Value, CallerKind.App, appId: "notes"));
        Assert.Equal(new Actor.App("notes", new Actor.Person("alice", "acct-1")), app.Actor);
        Assert.False(app.Inside);

        var guest = edge.From(new CapabilityCaller("try-1", "try-1", AliceBrain.Value, AppId: "guest"));
        Assert.Equal(new Actor.App("guest", new Actor.Person("try-1", "try-1")), guest.Actor);

        var scheduler = edge.From(CapabilityCallers.Of("s", "s", AliceBrain.Value, CallerKind.Scheduler));
        Assert.IsType<Actor.Scheduler>(scheduler.Actor);
        Assert.True(scheduler.Inside);
        Assert.False(scheduler.Direct);

        var kernel = edge.From(CapabilityCallers.Platform("k", "k", BrainId.Kernel.Value));
        Assert.IsType<Actor.Kernel>(kernel.Actor);
        Assert.Equal(BrainId.Kernel, kernel.Brain);
        Assert.True(kernel.Inside);
        Assert.False(kernel.Direct);
    }

    [Fact]
    public void KernelAndInProcessScopes_TrackAndRestoreAmbientStateCleanly()
    {
        var carrier = new AsyncLocalTestCarrier();
        var edge = EdgeAuthority.CreateInternal(carrier);

        Assert.Null(edge.Current);
        Assert.False(edge.IsKernelActive);

        using (edge.Enter(edge.Person("alice", "acct-1", AliceBrain)))
        {
            Assert.IsType<Actor.Person>(edge.Current?.Actor);
            Assert.False(edge.IsKernelActive);

            using (edge.EnterKernel(AliceBrain))
            {
                Assert.IsType<Actor.Kernel>(edge.Current?.Actor);
                Assert.True(edge.IsKernelActive);
            }

            Assert.IsType<Actor.Person>(edge.Current?.Actor);
            Assert.False(edge.IsKernelActive);

            using (edge.ClearCaller())
            {
                Assert.Null(edge.Current);
                Assert.True(edge.IsKernelActive);
            }

            Assert.IsType<Actor.Person>(edge.Current?.Actor);
            Assert.False(edge.IsKernelActive);
        }

        using (edge.EnterInProcess())
        {
            Assert.Null(edge.Current);
            Assert.True(edge.IsKernelActive);
        }

        Assert.Null(edge.Current);
        Assert.False(edge.IsKernelActive);
    }

    private sealed class AsyncLocalTestCarrier : ICallerCarrier
    {
        private readonly AsyncLocal<Caller?> _currentCaller = new();
        private readonly AsyncLocal<bool> _kernelScope = new();

        public Caller? Current => _currentCaller.Value;

        public bool IsKernelActive => _kernelScope.Value && _currentCaller.Value?.Actor is null or Actor.Kernel;

        public IDisposable Enter(Caller? caller)
        {
            var prevCaller = _currentCaller.Value;
            var prevKernel = _kernelScope.Value;
            _currentCaller.Value = caller;
            if (caller?.Actor is Actor.Kernel or Actor.Module or Actor.Scheduler)
            {
                _kernelScope.Value = true;
            }
            return new Restore(this, prevCaller, prevKernel);
        }

        public IDisposable EnterKernelScope(bool clearCaller)
        {
            var prevCaller = _currentCaller.Value;
            var prevKernel = _kernelScope.Value;
            if (clearCaller)
            {
                _currentCaller.Value = null;
            }
            _kernelScope.Value = true;
            return new Restore(this, prevCaller, prevKernel);
        }

        private sealed class Restore(AsyncLocalTestCarrier carrier, Caller? caller, bool kernel) : IDisposable
        {
            public void Dispose()
            {
                carrier._currentCaller.Value = caller;
                carrier._kernelScope.Value = kernel;
            }
        }
    }
}
