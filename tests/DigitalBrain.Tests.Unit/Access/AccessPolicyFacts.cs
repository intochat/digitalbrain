using DigitalBrain.Access;

namespace DigitalBrain.Tests.Unit.Access;

public sealed class AccessPolicyFacts
{
    private static readonly BrainId Alice = new("alice");
    private static readonly BrainId Bob = new("bob");
    private static readonly NeuronId AliceTarget = new(Alice, "wallet", "default");
    private static readonly NeuronId BobTarget = new(Bob, "wallet", "default");

    [Fact]
    public void KernelCallerIsExemptAcrossAllBrainsAndAccessLevels()
    {
        var kernelAlice = Caller.OfKernel(Alice);
        var kernelBob = Caller.OfKernel(Bob);

        foreach (var access in Enum.GetValues<OperationAccess>())
        {
            Assert.True(AccessPolicy.Decide(kernelAlice, AliceTarget, access).Allowed);
            Assert.True(AccessPolicy.Decide(kernelAlice, BobTarget, access).Allowed);
            Assert.True(AccessPolicy.Decide(kernelBob, AliceTarget, access).Allowed);
            Assert.True(AccessPolicy.Decide(kernelBob, BobTarget, access).Allowed);
        }
    }

    [Fact]
    public void CrossBrainCallIsRefusedWithOutsideBrainRegardlessOfAccessLevel()
    {
        var edge = EdgeAuthority.CreateInternal();
        var caller = edge.Person("alice-user", "acct_1", Bob); // Caller is in Bob's brain

        foreach (var access in Enum.GetValues<OperationAccess>())
        {
            var decision = AccessPolicy.Decide(caller, AliceTarget, access);
            Assert.False(decision.Allowed);
            Assert.Equal(AccessDenial.OutsideBrain, decision.Denial);
        }
    }

    [Fact]
    public void OwnerAndPublicOperationsAreAllowedForAnyActorInSameBrain()
    {
        var edge = EdgeAuthority.CreateInternal();
        var person = new Actor.Person("p1", "acct_1");
        Caller[] callers =
        [
            edge.Person("p1", "acct_1", Alice),
            edge.Agent(person, Alice),
            edge.App("app1", person, Alice),
            edge.Module("mod1", Alice),
            edge.Scheduler(Alice),
        ];

        foreach (var caller in callers)
        {
            foreach (var access in new[] { OperationAccess.Owner, OperationAccess.Public, OperationAccess.Shared })
            {
                var decision = AccessPolicy.Decide(caller, AliceTarget, access);
                Assert.True(decision.Allowed);
                Assert.Equal(AccessDenial.None, decision.Denial);
            }
        }
    }

    [Fact]
    public void InternalOperationRestrictedToModuleOrInside()
    {
        var edge = EdgeAuthority.CreateInternal();
        var person = new Actor.Person("p1", "acct_1");
        var personCaller = edge.Person("p1", "acct_1", Alice);
        var agentCaller = edge.Agent(person, Alice);
        var appCaller = edge.App("app1", person, Alice);
        var schedCaller = edge.Scheduler(Alice);
        var moduleCaller = edge.Module("mod1", Alice);

        // Outside non-module callers are denied
        Assert.False(AccessPolicy.Decide(personCaller, AliceTarget, OperationAccess.Internal).Allowed);
        Assert.Equal(AccessDenial.InternalOnly, AccessPolicy.Decide(personCaller, AliceTarget, OperationAccess.Internal).Denial);
        Assert.False(AccessPolicy.Decide(agentCaller, AliceTarget, OperationAccess.Internal).Allowed);
        Assert.False(AccessPolicy.Decide(appCaller, AliceTarget, OperationAccess.Internal).Allowed);

        // Module in same brain is allowed even without explicit Inside
        Assert.True(AccessPolicy.Decide(moduleCaller, AliceTarget, OperationAccess.Internal).Allowed);

        // Any actor with Inside=true in same brain is allowed
        Assert.True(AccessPolicy.Decide(personCaller.Hop(null, inside: true), AliceTarget, OperationAccess.Internal).Allowed);
        Assert.True(AccessPolicy.Decide(agentCaller.Hop(null, inside: true), AliceTarget, OperationAccess.Internal).Allowed);
        Assert.True(AccessPolicy.Decide(schedCaller.Hop(null, inside: true), AliceTarget, OperationAccess.Internal).Allowed);
    }
}
