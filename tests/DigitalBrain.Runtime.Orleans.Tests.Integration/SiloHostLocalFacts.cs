using DigitalBrain.Hosting;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Orleans;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration;

public sealed class SiloHostLocalFacts
{
    [Fact]
    public async Task SiloHost_Local_round_trips_platform_state_and_deletes_its_temp_home()
    {
        var ct = TestContext.Current.CancellationToken;
        string home;
        await using (var host = await SiloHost.Local(ct: ct))
        {
            home = host.Home;
            await host.Services.GetRequiredService<IGrainFactory>().GetGrain<IProbeGrain>("p1").Set(42);
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
            Assert.Equal(7, await host.Services.GetRequiredService<IGrainFactory>().GetGrain<IProbeGrain>("p2").Get());
        }
        Assert.Equal(1, fake.Registered);
        Assert.True(File.Exists(Path.Combine(home.Path, "secrets", MachineKeyConfigurationSource.FileName)));
    }

    [Fact]
    public async Task Platform_grain_storage_and_file_reminder_table_survive_silo_restart_and_fresh_host_reload()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        var reminderGrainId = global::Orleans.Runtime.GrainId.Create("confirmations", "b1|confirmations");
        var dueAt = DateTime.UtcNow.AddHours(48);

        await using (var host = await new LocalSiloHostFactory(new FakeClusterStore()).Start(new InProcessHostOptions { Home = home.Path }, ct))
        {
            await host.Services.GetRequiredService<IGrainFactory>().GetGrain<IProbeGrain>("persisted-cfg").Set(9001);
            var reminders = Assert.IsType<FileReminderTable>(host.Services.GetRequiredService<IReminderTable>());
            var etag = await reminders.UpsertRow(new global::Orleans.ReminderEntry
            {
                GrainId = reminderGrainId,
                ReminderName = "confirmation-expiry",
                StartAt = dueAt,
                Period = TimeSpan.FromDays(2),
            });
            Assert.False(string.IsNullOrEmpty(etag));
        }

        var remindersFile = Path.Combine(home.Path, "state", "reminders.json");
        Assert.True(File.Exists(remindersFile));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(home.Path, "state", "files", RuntimeOptions.Default.PlatformGrainStorage), "*.json"));

        // Fresh LocalSiloHostFactory instance on the same Home directory reloads both platform grain state and reminders from disk
        await using (var reloaded = await new LocalSiloHostFactory(new FakeClusterStore()).Start(new InProcessHostOptions { Home = home.Path }, ct))
        {
            var value = await reloaded.Services.GetRequiredService<IGrainFactory>().GetGrain<IProbeGrain>("persisted-cfg").Get();
            Assert.Equal(9001, value);

            var reminders = Assert.IsType<FileReminderTable>(reloaded.Services.GetRequiredService<IReminderTable>());
            var row = await reminders.ReadRow(reminderGrainId, "confirmation-expiry");
            Assert.NotNull(row);
            Assert.Equal(TimeSpan.FromDays(2), row.Period);
            Assert.Equal(dueAt.Ticks, row.StartAt.Ticks);
        }
    }
}
