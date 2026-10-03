using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Contracts.Identity;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Kernel.Tests.Unit.Identity;

public sealed class BrainEstablishmentFacts
{
    [Fact]
    public async Task RegisteringAMemberEstablishesTheirBrainNeuron()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().StartAsync(ct);
        var directory = brain.Get<IIdentityDirectory>(IdentityGrains.Directory);
        var member = await directory.RegisterAsync("alice", "correct-password", "Alice", ct);
        // renamed to BrainId in the identity rename task
        var snapshot = await brain.Get<IBrain>(BrainScope.Create(member.AccountId, member.BrainId).Id).Read();
        Assert.Equal(member.BrainId, snapshot.Name);
        Assert.Equal(member.AccountId, snapshot.OwnerAccountId);
    }

    [Fact]
    public async Task EstablishIsIdempotentWithIdentityComposed()
    {
        // Covers Identity-composed path; Core-level test exists separately in BrainFacts.cs
        var ct = TestContext.Current.CancellationToken;
        await using var host = await UnitTest.Create().StartAsync(ct);
        var neuron = host.Get<IBrain>(BrainScope.Create("acct", "personal").Id);
        var first = await neuron.Establish(new("personal", "acct"));
        var second = await neuron.Establish(new("personal", "acct"));
        Assert.Equal(first.EstablishedAt, second.EstablishedAt);
    }
}
