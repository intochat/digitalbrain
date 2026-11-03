using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration;

public sealed class NoPortMeansDeniedFacts
{
    [Fact]
    public async Task NoPortMeansDeniedNotLocal()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await SiloHost.Local(ct: ct);
        var broker = host.Services.GetRequiredService<ICapabilityBroker>();
        var brain = new BrainId("b1");
        var identity = await broker.Resolve<IIdentity>(brain, ct);
        var grants = await broker.Resolve<IGrants>(brain, ct);
        var member = new Member
        {
            PrincipalId = "bob",
            AccountId = "bob-acct",
            BrainId = "b1",
            Role = MemberRole.Member,
            DisplayName = "Bob",
        };
        var grant = new Grant
        {
            AppId = "calculator",
            SemanticTypeId = "math.op",
            Mode = GrantMode.Always,
            WorkspaceId = "b1",
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => identity.EnsureMember(member, ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => grants.GrantAsOwner(grant, ct));

        using (RuntimeCallerContext.Stamp(new CapabilityCaller("ada", "ada-acct", "b1")))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => identity.EnsureMember(member, ct));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => identity.RemoveMember("b1", "bob-acct", ct));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => identity.RemovePrincipalMember("b1", "bob", ct));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => grants.GrantAsOwner(grant, ct));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => grants.RevokeAsOwner("calculator", "math.op", GrantMode.Always, ct));
        }
    }
}
