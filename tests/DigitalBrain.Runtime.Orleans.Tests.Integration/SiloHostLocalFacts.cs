using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Orleans;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration;

public sealed class SiloHostLocalFacts
{
    [Fact]
    public async Task SiloHost_Local_round_trips_platform_state_survives_Restart_and_deletes_its_temp_home()
    {
        var ct = TestContext.Current.CancellationToken;
        string home;
        await using (var host = await SiloHost.Local(ct: ct))
        {
            home = host.Home;
            await host.Services.GetRequiredService<IGrainFactory>().GetGrain<IProbeGrain>("p1").Set(42);
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(home, "state", "files", IntoChatOSNames.PlatformGrainStorage)));

            await host.Restart(ct);

            Assert.Equal(42, await host.Services.GetRequiredService<IGrainFactory>().GetGrain<IProbeGrain>("p1").Get());
        }
        Assert.False(Directory.Exists(home));
    }

    [Fact]
    public async Task A_given_home_is_kept_and_a_host_store_registration_runs_on_every_start()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        var fake = new FakeClusterStore();
        await using (var host = await new LocalSiloHostFactory(fake).Start(new InProcessHostOptions { Home = home.Path }, ct))
        {
            await host.Services.GetRequiredService<IGrainFactory>().GetGrain<IProbeGrain>("p2").Set(7);
            await host.Restart(ct);
            Assert.Equal(7, await host.Services.GetRequiredService<IGrainFactory>().GetGrain<IProbeGrain>("p2").Get());
        }
        Assert.Equal(2, fake.Registered);
        Assert.True(File.Exists(Path.Combine(home.Path, "secrets", MachineKeyConfigurationSource.FileName)));
    }
}
