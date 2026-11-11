using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.Access;

namespace DigitalBrain.Tests.Unit.Access;

/// <summary>
/// Empirical adversarial stress tests for Milestone 4:
/// 1. Unforgeability of Caller and Actor (internal constructors, no public setters, JSON deserialization fails).
/// 2. AccessPolicy.Decide against adversarial inputs (cross-brain, unknown actors, external [Internal] access).
/// 3. Caller.Direct evaluation across direct human calls vs synthetic/internal/routed calls.
/// </summary>
public sealed class CallerAdversarialFacts
{
    private static readonly BrainId AttackerBrain = new("attacker-brain");
    private static readonly BrainId VictimBrain = new("victim-brain");
    private static readonly NeuronId VictimTarget = new(VictimBrain, "vault", "default");

    // =========================================================================
    // Area 1: Attempt to Forge a Caller / Actor
    // =========================================================================

    [Fact]
    public void Caller_HasNoPublicConstructors()
    {
        var publicCtors = typeof(Caller).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicCtors);
    }

    [Fact]
    public void Actor_HasNoPublicConstructors_AndCannotBeSubclassedExternally()
    {
        var publicCtors = typeof(Actor).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicCtors);

        var defaultCtor = typeof(Actor).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
        Assert.NotNull(defaultCtor);
        Assert.True(defaultCtor.IsPrivate, "Actor default constructor must be strictly private to prevent external subclassing.");

        // Direct instantiation via CLR uninitialized allocation must also fail because Actor is abstract
        Assert.Throws<MemberAccessException>(() => RuntimeHelpers.GetUninitializedObject(typeof(Actor)));

        // The closed hierarchy must contain only the 6 authorized actor types
        var nestedTypes = typeof(Actor).GetNestedTypes(BindingFlags.Public);
        var expectedTypes = new[]
        {
            typeof(Actor.Person),
            typeof(Actor.Agent),
            typeof(Actor.App),
            typeof(Actor.Module),
            typeof(Actor.Scheduler),
            typeof(Actor.Kernel)
        };
        Assert.Equal(expectedTypes.OrderBy(t => t.Name), nestedTypes.OrderBy(t => t.Name));
    }

    [Fact]
    public void EdgeAuthority_HasNoPublicConstructors()
    {
        var publicCtors = typeof(EdgeAuthority).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicCtors);
    }

    [Fact]
    public void Caller_PropertiesHaveNoPublicSetters()
    {
        var properties = typeof(Caller).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.NotEmpty(properties);

        foreach (var prop in properties)
        {
            var setMethod = prop.GetSetMethod(nonPublic: true);
            Assert.True(setMethod is null, $"Property {prop.Name} on Caller must not have any set or init accessor.");
            Assert.False(prop.CanWrite, $"Property {prop.Name} on Caller must be read-only.");
        }
    }

    [Fact]
    public void Caller_JsonDeserialization_ThrowsNotSupportedException()
    {
        // Attempting to deserialize Caller from JSON must fail because it has no public constructor.
        var emptyJson = "{}";
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<Caller>(emptyJson));

        var forgedJson = """
        {
            "Actor": { "Principal": "attacker", "Account": "acct_evil" },
            "Brain": { "Value": "victim-brain" },
            "Inside": true
        }
        """;
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<Caller>(forgedJson));
    }

    [Fact]
    public void Actor_JsonDeserialization_ThrowsNotSupportedException()
    {
        var json = """{"Principal":"evil","Account":"evil"}""";
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<Actor>(json));
    }

    [Fact]
    public void UninitializedCaller_PassedToAccessPolicy_RejectedAsUnknownActor()
    {
        // An attacker attempting to bypass constructors using RuntimeHelpers.GetUninitializedObject
        // creates a Caller where Actor is null / forged.
        var uninitializedCaller = (Caller)RuntimeHelpers.GetUninitializedObject(typeof(Caller));
        Assert.Null(uninitializedCaller.Actor);

        var decision = AccessPolicy.Decide(uninitializedCaller, VictimTarget, OperationAccess.Public);
        Assert.False(decision.Allowed);
        Assert.Equal(AccessDenial.UnknownActor, decision.Denial);
        Assert.Equal("The caller's actor is not one the kernel knows.", decision.Explanation);
    }

    [Fact]
    public void AccessPolicy_NullCaller_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => AccessPolicy.Decide(null!, VictimTarget, OperationAccess.Owner));
    }

    // =========================================================================
    // Area 2: AccessPolicy Adversarial Stress Tests
    // =========================================================================

    [Theory]
    [InlineData(OperationAccess.Owner)]
    [InlineData(OperationAccess.Public)]
    [InlineData(OperationAccess.Shared)]
    [InlineData(OperationAccess.Internal)]
    public void CrossBrainCalls_AllNonKernelActors_StrictlyDeniedWithOutsideBrain(OperationAccess access)
    {
        var edge = EdgeAuthority.CreateInternal();
        var person = new Actor.Person("attacker", "attacker-acct");

        Caller[] adversarialCallers =
        [
            edge.Person("attacker", "attacker-acct", AttackerBrain),
            edge.Agent(person, AttackerBrain),
            edge.App("evil-app", person, AttackerBrain),
            edge.Module("evil-module", AttackerBrain),
            edge.Scheduler(AttackerBrain),
            // Hopped cross-brain caller
            edge.Person("attacker", "attacker-acct", AttackerBrain).Hop(VictimTarget, inside: false),
            // Forged inside flag on cross-brain caller
            edge.Person("attacker", "attacker-acct", AttackerBrain).Hop(null, inside: true),
        ];

        foreach (var caller in adversarialCallers)
        {
            var decision = AccessPolicy.Decide(caller, VictimTarget, access);
            Assert.False(decision.Allowed, $"Caller {caller.Actor.GetType().Name} with Inside={caller.Inside} must be denied cross-brain.");
            Assert.Equal(AccessDenial.OutsideBrain, decision.Denial);
            Assert.Contains(AttackerBrain.Value, decision.Explanation, StringComparison.Ordinal);
            Assert.Contains(VictimBrain.Value, decision.Explanation, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ExternalCalls_ToInternalOperations_StrictlyDeniedWithInternalOnly()
    {
        var edge = EdgeAuthority.CreateInternal();
        var person = new Actor.Person("user1", "acct1");

        // External callers (Inside = false) in same brain
        Caller[] externalCallers =
        [
            edge.Person("user1", "acct1", VictimBrain),
            edge.Agent(person, VictimBrain),
            edge.App("app1", person, VictimBrain),
            // Hopped call that did not enter cluster
            edge.Person("user1", "acct1", VictimBrain).Hop(new NeuronId(VictimBrain, "router", "default"), inside: false),
        ];

        foreach (var caller in externalCallers)
        {
            var decision = AccessPolicy.Decide(caller, VictimTarget, OperationAccess.Internal);
            Assert.False(decision.Allowed, $"External caller {caller.Actor.GetType().Name} must not access [Internal] operation.");
            Assert.Equal(AccessDenial.InternalOnly, decision.Denial);
            Assert.Contains("reachable only from inside the cluster", decision.Explanation, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void InternalOperations_PermittedForInsideCallersAndModulesInSameBrain()
    {
        var edge = EdgeAuthority.CreateInternal();
        var person = new Actor.Person("user1", "acct1");

        // Module in same brain is allowed even if Inside = false
        var moduleCaller = new Caller(new Actor.Module("sys.mod"), VictimBrain, inside: false);
        Assert.True(AccessPolicy.Decide(moduleCaller, VictimTarget, OperationAccess.Internal).Allowed);

        // Internal callers (Inside = true) in same brain
        var insidePerson = edge.Person("user1", "acct1", VictimBrain).Hop(null, inside: true);
        var insideAgent = edge.Agent(person, VictimBrain).Hop(null, inside: true);
        var insideApp = edge.App("app1", person, VictimBrain).Hop(null, inside: true);
        var insideScheduler = edge.Scheduler(VictimBrain);

        Assert.True(AccessPolicy.Decide(insidePerson, VictimTarget, OperationAccess.Internal).Allowed);
        Assert.True(AccessPolicy.Decide(insideAgent, VictimTarget, OperationAccess.Internal).Allowed);
        Assert.True(AccessPolicy.Decide(insideApp, VictimTarget, OperationAccess.Internal).Allowed);
        Assert.True(AccessPolicy.Decide(insideScheduler, VictimTarget, OperationAccess.Internal).Allowed);
    }

    [Fact]
    public void InternalOperations_DeniedCrossBrain_EvenIfInsideIsTrue()
    {
        var edge = EdgeAuthority.CreateInternal();
        var insideCrossBrain = edge.Person("user1", "acct1", AttackerBrain).Hop(null, inside: true);

        var decision = AccessPolicy.Decide(insideCrossBrain, VictimTarget, OperationAccess.Internal);
        Assert.False(decision.Allowed);
        Assert.Equal(AccessDenial.OutsideBrain, decision.Denial);
    }

    // =========================================================================
    // Area 4: Caller.Direct Verification
    // =========================================================================

    [Fact]
    public void Caller_Direct_EvaluatesTrue_OnlyForDirectHumanEdgeCalls()
    {
        var edge = EdgeAuthority.CreateInternal();
        var person = new Actor.Person("human", "acct1");

        var directPerson = edge.Person("human", "acct1", VictimBrain);
        var directAgent = edge.Agent(person, VictimBrain);
        var directApp = edge.App("app1", person, VictimBrain);

        Assert.True(directPerson.Direct, "Direct person edge call must have Direct == true.");
        Assert.True(directAgent.Direct, "Direct agent edge call must have Direct == true.");
        Assert.True(directApp.Direct, "Direct app edge call must have Direct == true.");
    }

    [Fact]
    public void Caller_Direct_EvaluatesFalse_ForSyntheticAndInternalCalls()
    {
        var edge = EdgeAuthority.CreateInternal();
        var person = new Actor.Person("human", "acct1");
        var hopNeuron = new NeuronId(VictimBrain, "hop", "default");

        // 1. Kernel calls (synthetic/internal)
        var kernelCaller = Caller.OfKernel(VictimBrain);
        Assert.False(kernelCaller.Direct, "Kernel caller must have Direct == false.");

        // 2. Module calls (internal host components)
        var moduleCaller = edge.Module("auth-module", VictimBrain);
        Assert.False(moduleCaller.Direct, "Module caller must have Direct == false.");

        // 3. Scheduler calls (infrastructure timers)
        var schedulerCaller = edge.Scheduler(VictimBrain);
        Assert.False(schedulerCaller.Direct, "Scheduler caller must have Direct == false.");

        // 4. Hopped internal calls (Via != null, Inside == true)
        var hoppedInternal = edge.Person("human", "acct1", VictimBrain).Hop(hopNeuron, inside: true);
        Assert.False(hoppedInternal.Direct, "Hopped internal caller must have Direct == false.");

        // 5. Hopped external calls (Via != null, Inside == false)
        var hoppedExternal = edge.Person("human", "acct1", VictimBrain).Hop(hopNeuron, inside: false);
        Assert.False(hoppedExternal.Direct, "Hopped external caller must have Direct == false.");

        // 6. Direct caller synthetic hop to Inside without Via
        var insideNoVia = edge.Person("human", "acct1", VictimBrain).Hop(null, inside: true);
        Assert.False(insideNoVia.Direct, "Inside caller without Via must have Direct == false.");
    }
}
