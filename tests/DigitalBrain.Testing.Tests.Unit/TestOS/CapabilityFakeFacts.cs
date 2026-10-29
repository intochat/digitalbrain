using DigitalBrain.Capabilities;
using DigitalBrain.Testing.Tests.Unit.Apps;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Hosting;

// 04-09 Task 1: the capability fakes registry (06 D-08, T-04-26).
public sealed class CapabilityFakeFacts
{
    private static readonly NeuronId Counter = new(new BrainId("ada"), "sample-app.counter", "one");

    [Fact]
    public async Task A_bound_fake_reaches_the_neuron_and_a_second_binding_of_the_same_name_throws()
    {
        var c = new StorageLikeFake("C:");
        await using var os = OS.Local().With<IStorageLike>("c", c);
        os.Register<SampleCounter>();

        Assert.Equal("C:", await os.Get<ISampleCounter>(Counter).StorageLabel());
        Assert.Same(c, os.Capabilities.Resolve<IStorageLike>("c"));
        Assert.Equal([CapabilityId.Parse("storage-like@1")], os.Capabilities.Bound);
        var duplicate = Assert.Throws<ArgumentException>(() => os.With<IStorageLike>("c", new StorageLikeFake("other")));
        Assert.Contains("storage-like@1", duplicate.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unbound_capability_fails_with_its_name_and_version()
    {
        await using var os = OS.Local().With<IStorageLike>("c", new StorageLikeFake("C:"));
        os.Register<SampleCounter>();

        var byName = Assert.Throws<CapabilityNotBoundException>(() => os.Capabilities.Resolve<IStorageLike>("drive"));
        Assert.Equal("drive", byName.Name);
        Assert.Contains("storage-like@1", byName.Message, StringComparison.Ordinal);

        var unbound = Assert.Throws<CapabilityNotBoundException>(() => os.Capabilities.Resolve<IClockLike>());
        Assert.Equal(CapabilityId.Parse("clock-like@1"), unbound.Capability);

        await using var empty = OS.Local();
        empty.Register<SampleCounter>();
        var neuron = await Assert.ThrowsAsync<CapabilityNotBoundException>(() => empty.Get<ISampleCounter>(Counter).StorageLabel());
        Assert.Contains("storage-like@1", neuron.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_contract_without_the_capability_attribute_is_refused()
    {
        await using var os = OS.Local();

        var error = Assert.Throws<ArgumentException>(() => os.With<INotACapability>("x", new NotACapability()));
        Assert.Contains("[Capability(name, version)]", error.Message, StringComparison.Ordinal);
    }

    private sealed class NotACapability : INotACapability
    {
        public void Nothing()
        {
        }
    }
}
