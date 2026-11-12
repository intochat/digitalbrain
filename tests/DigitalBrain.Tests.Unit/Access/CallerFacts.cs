using System.Reflection;
using DigitalBrain.Access;
using DigitalBrain.Capabilities;

namespace DigitalBrain.Tests.Unit.Access;

public sealed class CallerFacts
{
    private static readonly BrainId Alice = new("alice");
    private static readonly BrainId Bob = new("bob");

    [Fact]
    public void CallerHasNoPublicConstructors()
    {
        var publicCtors = typeof(Caller).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicCtors);
    }

    [Fact]
    public void ActorHasNoPublicConstructors()
    {
        var publicCtors = typeof(Actor).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicCtors);
    }

    [Fact]
    public void KernelCallerHasExpectedDefaults()
    {
        var kernel = Caller.OfKernel(Alice);

        Assert.IsType<Actor.Kernel>(kernel.Actor);
        Assert.Equal(Alice, kernel.Brain);
        Assert.Null(kernel.Conversation);
        Assert.Null(kernel.Via);
        Assert.True(kernel.Inside);
        Assert.False(kernel.Direct);
    }

    [Fact]
    public void EdgeAuthorityMintsDirectCallersWithInsideFalse()
    {
        var edge = EdgeAuthority.CreateInternal();
        var person = edge.Person("alice_p", "acct_1", Alice, "conv_1");

        Assert.Equal("alice_p", ((Actor.Person)person.Actor).Principal);
        Assert.Equal("acct_1", ((Actor.Person)person.Actor).Account);
        Assert.Equal(Alice, person.Brain);
        Assert.Equal("conv_1", person.Conversation);
        Assert.Null(person.Via);
        Assert.False(person.Inside);
        Assert.True(person.Direct);
    }

    [Fact]
    public void HopIntraBrainSetsInsideTrueAndSetsVia()
    {
        var edge = EdgeAuthority.CreateInternal();
        var person = edge.Person("alice_p", "acct_1", Alice);
        var targetNeuron = new NeuronId(Alice, "wallet", "default");

        var hopped = person.Hop(targetNeuron, inside: true);

        Assert.True(hopped.Inside);
        Assert.Equal(targetNeuron, hopped.Via);
        Assert.False(hopped.Direct);
    }

    [Fact]
    public void HopCrossBrainSetsViaAndClearsInside()
    {
        var edge = EdgeAuthority.CreateInternal();
        var person = edge.Person("alice_p", "acct_1", Alice);
        var bobNeuron = new NeuronId(Bob, "wallet", "default");

        var hopped = person.Hop(bobNeuron, inside: false);

        Assert.False(hopped.Inside);
        Assert.Equal(bobNeuron, hopped.Via);
        Assert.False(hopped.Direct);
    }

    [Fact]
    public void ForPropertyResolvesDelegatedHumanPerson()
    {
        var edge = EdgeAuthority.CreateInternal();
        var person = new Actor.Person("user_99", "acct_99");
        var app = edge.App("payment_app", person, Alice);

        Assert.Equal(person, app.For);
        Assert.Equal("user_99", app.For?.Principal);
    }

    [Fact]
    public void LegacyCallersBidirectionalMappingPreservesIdentity()
    {
        var edge = EdgeAuthority.CreateInternal();
        var person = edge.Person("user_1", "acct_1", Alice, "conv_42");

        var legacy = LegacyCallers.Project(person);
        Assert.NotNull(legacy);
        Assert.Equal("user_1", legacy.PrincipalId);
        Assert.Equal("acct_1", legacy.AccountId);
        Assert.Equal(Alice.ToString(), legacy.BrainId);
        Assert.Equal("conv_42", legacy.ConversationId);

        var roundtrip = LegacyCallers.Read(legacy);
        Assert.Equal(person.Actor, roundtrip.Actor);
        Assert.Equal(person.Brain, roundtrip.Brain);
        Assert.Equal(person.Conversation, roundtrip.Conversation);
    }
}
