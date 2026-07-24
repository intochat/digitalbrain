using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Runtime;
using Orleans.Runtime.Services;
using Orleans.TestingHost;

namespace DigitalBrain.Testing;

internal sealed class FixtureCluster : IAsyncDisposable
{
    private const int SiloCount = 3;

    private readonly RecordingJournalStorageProvider _journalStorage =
        new(new VolatileJournalStorageProvider());
    private readonly IReadOnlyCollection<ICompiledModule> _modules;
    private readonly VolatileReminderTable _reminderTable = new();
    private readonly ScenarioClock _clock = new();
#pragma warning disable CA2213 // The cluster is disposed asynchronously in DisposeAsync.
    private InProcessTestCluster? _cluster;
#pragma warning restore CA2213

    private FixtureCluster(IReadOnlyCollection<ICompiledModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        _modules = modules.ToArray();
    }

    internal static async Task<FixtureCluster> StartAsync(
        IReadOnlyCollection<ICompiledModule> modules)
    {
        var fixture = new FixtureCluster(modules);

        try
        {
            await fixture.StartCoreAsync();
            return fixture;
        }
        catch (Exception startupFailure)
        {
            try
            {
                await fixture.DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "The DigitalBrain fixture cluster failed during startup and cleanup.",
                    startupFailure,
                    cleanupFailure);
            }

            throw;
        }
    }

    internal IGrainFactory Client
        => _cluster?.Client
            ?? throw new InvalidOperationException(
                "The DigitalBrain fixture cluster is not running.");

    internal static string LabelOf(string siloName)
    {
        var separator = siloName.LastIndexOf('_');
        var instance = short.Parse(
            siloName.AsSpan(separator + 1),
            System.Globalization.CultureInfo.InvariantCulture);

        return (instance % SiloCount) switch
        {
            0 => "alpha",
            1 => "beta",
            _ => "gamma",
        };
    }

    public async ValueTask DisposeAsync()
    {
        var cluster = Interlocked.Exchange(ref _cluster, null);
        if (cluster is null)
        {
            return;
        }

        try
        {
            await cluster.StopAllSilosAsync();
        }
        finally
        {
            await cluster.DisposeAsync();
        }
    }

    private async Task StartCoreAsync()
    {
        var builder = new InProcessTestClusterBuilder(SiloCount);
        builder.ConfigureSilo((options, silo) =>
        {
            var moduleIndex = 0;
            foreach (var module in _modules)
            {
                silo.Configuration[$"DigitalBrain:Modules:{moduleIndex}"] = module.Id.Value;
                moduleIndex++;
            }

            DigitalBrainRuntime.Add(silo, FixtureCluster.LabelOf(options.SiloName), _modules);
            silo.UseInMemoryReminderService();
            silo.Services.AddGrainService<SpoofReminderService>();
            silo.Services.AddSingleton<ISpoofReminderServiceClient, SpoofReminderServiceClient>();
            silo.Services.AddSingleton<IReminderTable>(_reminderTable);
            silo.Services.Configure<ReminderOptions>(reminders =>
            {
                reminders.InitializationTimeout = TimeSpan.FromSeconds(1);
                reminders.MinimumReminderPeriod = TimeSpan.FromMilliseconds(50);
                reminders.RefreshReminderListPeriod = TimeSpan.FromMilliseconds(50);
            });
            silo.Services.AddSingleton<IJournalStorageProvider>(_journalStorage);
            silo.Services.AddSingleton<TimeProvider>(_clock);
        });
        builder.ConfigureClient(client =>
        {
            foreach (var module in _modules)
            {
                module.PrepareSerialization(client.Services);
            }
        });

        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }
}
