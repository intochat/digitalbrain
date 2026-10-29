using DigitalBrain.Capabilities;
using DigitalBrain.Testing.Tests.Unit.Apps;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Hosting;

// 04-09 Task 1: Install<TApp>() and request driving.
public sealed class InstallAppFacts
{
    [Fact]
    public async Task An_app_with_its_requires_bound_installs_and_an_operation_runs_as_a_turn_calling_the_fake()
    {
        var c = new StorageLikeFake("C:", used: 10);
        await using var os = OS.Local().With<IStorageLike>("c", c);

        var app = await os.Install<SampleApp>();
        await app.Request("open");
        _ = app.Request("rename", "notes");
        await os.Idle();

        Assert.Equal("sample-app", app.Manifest.Id);
        Assert.Equal([CapabilityId.Parse("storage-like@1")], app.Manifest.Requires.Capabilities);
        Assert.Contains(typeof(SampleCounter), app.Manifest.Neurons);
        Assert.Equal(["Open", "Rename"], app.Operations);
        Assert.Equal(1, c.Calls);
        Assert.Equal(["open:C::10:True", "rename:notes"], app.App.Log);
        Assert.Equal(2, await os.Get<ISampleCounter>(new NeuronId(new BrainId("ada"), "sample-app.counter", "k")).Count(2));
        Assert.Throws<ArgumentException>(() => { _ = app.Request("missing"); });
        Assert.Empty(os.Failures);
    }

    [Fact]
    public async Task A_missing_required_capability_fails_install_naming_it()
    {
        await using var os = OS.Local().With<IStorageLike>("c", new StorageLikeFake("C:"));

        var error = await Assert.ThrowsAsync<CapabilityNotBoundException>(() => os.Install<NeedsClockApp>());

        Assert.Equal(CapabilityId.Parse("clock-like@1"), error.Capability);
        Assert.Contains("App 'needs-clock' requires capability clock-like@1", error.Message, StringComparison.Ordinal);
        await using var bare = OS.Local();
        var unbound = await Assert.ThrowsAsync<CapabilityNotBoundException>(() => bare.Install<SampleApp>());
        Assert.Contains("storage-like@1", unbound.Message, StringComparison.Ordinal);
    }
}
