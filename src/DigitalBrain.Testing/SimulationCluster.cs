using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.TestingHost;

namespace DigitalBrain.Testing;

public static class SimulationCluster
{
    private const int SiloCount = 3;

    private static readonly string[] SiloLabels = ["alpha", "beta", "gamma"];


    private static InProcessTestCluster? _cluster;
    private static SynapseObserver? _observer;

    public static IGrainFactory Grains => Deployed().Client;

    public static SynapseObserver Observed => _observer
        ?? throw new InvalidOperationException($"The simulation cluster is not running. Call {nameof(SimulationCluster)}.{nameof(StartAsync)} before a scenario runs.");

    public static async Task StartAsync()
    {
        if (_cluster is not null)
        {
            return;
        }

        var journalStorage = new VolatileJournalStorageProvider();
        var reminderTable = new VolatileReminderTable();
        var builder = new InProcessTestClusterBuilder(SiloCount);
        var handlerAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => SynapseWiring.TryGetManifest(assembly, out _))
            .ToArray();

        builder.ConfigureSilo((options, silo) =>
        {
            silo.AddDigitalBrain(LabelOf(options.SiloName));

            foreach (var assembly in handlerAssemblies)
            {
                silo.AddBroadcastHandlers(assembly);
            }

            silo.UseInMemoryReminderService();
            silo.Services.AddSingleton<IReminderTable>(reminderTable);
            silo.Services.Configure<ReminderOptions>(reminders =>
            {
                reminders.InitializationTimeout = TimeSpan.FromSeconds(1);
                reminders.MinimumReminderPeriod = TimeSpan.FromMilliseconds(50);
                reminders.RefreshReminderListPeriod = TimeSpan.FromMilliseconds(50);
            });
            silo.Services.AddSingleton<IJournalStorageProvider>(journalStorage);
        });

        var cluster = builder.Build();
        await cluster.DeployAsync();

        _observer = new SynapseObserver();
        _cluster = cluster;
    }

    public static async Task RestartHostOfAsync(NeuronId neuron)
    {
        var cluster = Deployed();
        var management = cluster.Client.GetGrain<IManagementGrain>(0);
        var hosting = (await management.GetDetailedGrainStatistics())
            .FirstOrDefault(statistic => statistic.GrainId == neuron.ToGrainId())?.SiloAddress
            ?? throw new InvalidOperationException($"{neuron} is not activated on any silo, so no host can be restarted.");

        var host = cluster.Silos.Single(silo => silo.SiloAddress.Equals(hosting));

        await cluster.RestartSiloAsync(host);
    }

    public static async Task StopAsync()
    {
        if (_cluster is null)
        {
            return;
        }

        _observer?.Dispose();
        _observer = null;

        await _cluster.StopAllSilosAsync();
        await _cluster.DisposeAsync();
        _cluster = null;
    }

    internal static string LabelOf(string siloName)
        => LabelOfInstance(short.Parse(
            siloName.AsSpan(siloName.LastIndexOf('_') + 1),
            System.Globalization.CultureInfo.InvariantCulture));

    private static string LabelOfInstance(short instance) => SiloLabels[instance % SiloLabels.Length];

    private static InProcessTestCluster Deployed() => _cluster
        ?? throw new InvalidOperationException($"The simulation cluster is not running. Call {nameof(SimulationCluster)}.{nameof(StartAsync)} before a scenario runs.");
}
