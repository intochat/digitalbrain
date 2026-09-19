global using ITimer = DigitalBrain.Time.ITimer;
using DigitalBrain.Time;
namespace DigitalBrain.Tests;

internal static class TimerTestSupport
{
    public static Task<BrainTestHost> StartAsync(CancellationToken ct, string? directory = null, StorageFaults? faults = null)
        => BrainTestHost.StartAsync(new()
        {
            PersistenceDirectory = directory,
            StorageFaults = faults,
            ConfigureSilo = silo => silo.AddTime()
        }, ct);
}


