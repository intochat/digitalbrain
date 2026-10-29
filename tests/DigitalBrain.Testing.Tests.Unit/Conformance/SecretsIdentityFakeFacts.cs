using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Capabilities.Secrets;

namespace DigitalBrain.Testing.Tests.Unit.Conformance;

public sealed class SecretsIdentityFakeFacts
{
    [Fact]
    public async Task WithSecrets_binds_a_fake_that_records_calls()
    {
        FakeSecrets? fake = null;
        await using var os = TestOS.Local().WithSecrets("vault", configure: f => fake = f);
        var broker = (ICapabilityBroker)os.Services.GetService(typeof(ICapabilityBroker))!;
        var all = await broker.ResolveAll<ISecrets>(new BrainId("ada"), TestContext.Current.CancellationToken);

        Assert.Equal(["vault"], all.Select(connection => connection.ConnectionKey));
        var secret = await all[0].Capability.Set("api-key", "API key", "s3cret", TestContext.Current.CancellationToken);
        Assert.Equal("s3cret", await all[0].Capability.Resolve(secret, TestContext.Current.CancellationToken));
        Assert.Equal([nameof(ISecrets.Set), nameof(ISecrets.Resolve)], fake!.Calls);
    }

    [Fact]
    public async Task WithIdentity_binds_both_identity_halves_under_one_name()
    {
        await using var os = TestOS.Local().WithIdentity("identity");
        var broker = (ICapabilityBroker)os.Services.GetService(typeof(ICapabilityBroker))!;
        var identity = await broker.ResolveAll<IIdentity>(new BrainId("ada"), TestContext.Current.CancellationToken);
        var grants = await broker.ResolveAll<IGrants>(new BrainId("ada"), TestContext.Current.CancellationToken);

        Assert.Equal(["identity"], identity.Select(connection => connection.ConnectionKey));
        Assert.Equal(["identity"], grants.Select(connection => connection.ConnectionKey));
        Assert.Same(identity[0].Capability, grants[0].Capability);
    }

    [Fact]
    public async Task FakeIdentity_registers_an_owner_and_records_calls()
    {
        var fake = new FakeIdentity();
        var owner = await fake.RegisterPrincipal("ada", "Ada", TestContext.Current.CancellationToken);

        Assert.True(owner.NewBrain);
        Assert.Equal(MemberRole.Owner, owner.Member.Role);
        Assert.Contains(nameof(IIdentity.RegisterPrincipal), fake.Calls);
    }
}
