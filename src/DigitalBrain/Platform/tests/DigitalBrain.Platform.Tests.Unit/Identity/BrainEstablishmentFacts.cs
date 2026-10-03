using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Contracts.Identity;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Platform.Tests.Unit.Identity;

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

}
