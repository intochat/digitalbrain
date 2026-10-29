using DigitalBrain.Capabilities;
using DigitalBrain.Testing.Tests.Unit.Apps;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Hosting;

// 05-01: ICapabilityBroker.ResolveAll (many connections, binding order) and single-connection Resolve.
public sealed class CapabilityBrokerFacts
{
    private static readonly BrainId Ada = new("ada");

    [Fact]
    public async Task TestOS_broker_lists_every_bound_fake_as_a_connection_in_binding_order()
    {
        var c = new StorageLikeFake("C:");
        var drive = new StorageLikeFake("Drive");
        await using var os = OS.Local().With<IStorageLike>("c", c).With<IStorageLike>("drive", drive);
        var broker = (ICapabilityBroker)os.Services.GetService(typeof(ICapabilityBroker))!;
        var ct = TestContext.Current.CancellationToken;

        var all = await broker.ResolveAll<IStorageLike>(Ada, ct);
        Assert.Equal(["c", "drive"], all.Select(connection => connection.ConnectionKey));
        Assert.Same(drive, all[1].Capability);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => broker.Resolve<IStorageLike>(Ada, ct));
        Assert.Empty(await broker.ResolveAll<IClockLike>(Ada, ct));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => broker.Resolve<IClockLike>(Ada, ct));
    }

    [Fact]
    public async Task Fake_broker_resolves_one_connection_and_lists_many()
    {
        var ct = TestContext.Current.CancellationToken;
        var broker = new FakeCapabilityBroker().Register<IStorageLike>(new StorageLikeFake("C:"), "c", "Local disk");
        Assert.Equal("C:", (await broker.Resolve<IStorageLike>(Ada, ct)).Label);
        Assert.Equal("Local disk", Assert.Single(await broker.ResolveAll<IStorageLike>(Ada, ct)).Label);

        broker.Register<IStorageLike>(new StorageLikeFake("Drive"));
        Assert.Equal(2, (await broker.ResolveAll<IStorageLike>(Ada, ct)).Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => broker.Resolve<IStorageLike>(Ada, ct));
    }
}
