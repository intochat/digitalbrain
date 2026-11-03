using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Identity.Local.Directory;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Conformance;

namespace DigitalBrain.Identity.Local.Tests.Unit;

public sealed class LocalOwnerFacts
{
    [Fact]
    public async Task EnsureCreatesOneOwnerAndOnePersonalBrain()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var os = TestOS.Local();
        os.Register<DirectoryNeuron>();
        var port = new LocalDirectoryPort((contract, fullKey) => LocalIdentityConformance.ResolveNeuron(os, contract, fullKey));
        var identity = new LocalIdentityConformance.PortIdentity(port, new AmbientCallerScope());

        var member = await LocalOwner.Ensure(identity, ct);

        Assert.Equal("local", member.AccountId);
        Assert.Equal("owner", member.PrincipalId);
        Assert.Equal(MemberRole.Owner, member.Role);
        Assert.NotNull(await identity.ReadAccount("local", ct));
        var brains = await identity.BrainsOf("owner", ct);
        var brain = Assert.Single(brains);
        Assert.Equal(BrainKind.Personal, brain.Kind);
    }

    [Fact]
    public async Task EnsureIsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var os = TestOS.Local();
        os.Register<DirectoryNeuron>();
        var port = new LocalDirectoryPort((contract, fullKey) => LocalIdentityConformance.ResolveNeuron(os, contract, fullKey));
        var identity = new LocalIdentityConformance.PortIdentity(port, new AmbientCallerScope());

        var first = await LocalOwner.Ensure(identity, ct);
        var second = await LocalOwner.Ensure(identity, ct);

        Assert.Equal(first.BrainId, second.BrainId);
        Assert.Equal(first.AccountId, second.AccountId);
        Assert.Equal(first.PrincipalId, second.PrincipalId);
        Assert.Single(await identity.BrainsOf("owner", ct));
    }
}
