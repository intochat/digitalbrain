using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Identity;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Modules.Identity.Tests.Unit;

public sealed class BrainFacts
{
    [Fact]
    public async Task RegisteringAMemberEstablishesTheirBrainNeuron()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<IdentityModule>().StartAsync(ct);
        var directory = brain.Get<IIdentityDirectory>(IdentityGrains.Directory);
        var member = await directory.RegisterAsync("alice", "correct-password", "Alice", ct);
        // renamed to BrainId in the identity rename task
        var snapshot = await brain.Get<IBrain>(BrainScope.Create(member.AccountId, member.WorkspaceId).Id).Read();
        Assert.Equal(member.WorkspaceId, snapshot.Name);
        Assert.Equal(member.AccountId, snapshot.OwnerAccountId);
    }

    [Fact]
    public async Task EstablishIsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await UnitTest.Create().WithModule<IdentityModule>().StartAsync(ct);
        var neuron = host.Get<IBrain>(BrainScope.Create("acct", "personal").Id);
        var first = await neuron.Establish(new("personal", "acct"));
        var second = await neuron.Establish(new("personal", "acct"));
        Assert.Equal(first.EstablishedAt, second.EstablishedAt);
    }
}
