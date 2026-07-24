using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace DigitalBrain.Testing;

internal static class SimulationClusterHost
{
    private const int SiloCount = 3;

    private static readonly string[] SiloLabels = ["alpha", "beta", "gamma"];
    private static readonly List<Func<Type, bool>> JsonSerializerPredicates = [];
    private static readonly SemaphoreSlim StartGate = new(1, 1);

    private static InProcessTestCluster? _cluster;
    private static SynapseObserver? _observer;
    private static RecordingJournalStorageProvider? _journalStorage;
    private static ScenarioClock? _clock;

    private static bool IsDeployed => _cluster is not null;

    public static IGrainFactory Grains => Deployed().Client;

    public static ScenarioClock Clock => _clock
        ?? throw new InvalidOperationException(
            $"The simulation cluster is not running. Call {nameof(SimulationCluster)}.{nameof(SimulationCluster.StartAsync)} before a scenario runs.");

    public static SynapseObserver Observed => _observer
        ?? throw new InvalidOperationException(
            $"The simulation cluster is not running. Call {nameof(SimulationCluster)}.{nameof(SimulationCluster.StartAsync)} before a scenario runs.");

    public static long CompletedJournalWrites(GrainId grain)
        => JournalStorage().CompletedWrites(grain);

    public static void FailJournalWriteAfter(
        GrainId grain,
        int completedWritesBeforeFailure,
        string message)
        => JournalStorage().FailWriteAfter(grain, completedWritesBeforeFailure, message);

    public static void ClearJournalWriteFailure(GrainId grain)
        => JournalStorage().ClearFailure(grain);

    public static async Task EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        if (IsDeployed)
        {
            return;
        }

        await StartGate.WaitAsync(cancellationToken);
        try
        {
            if (IsDeployed)
            {
                return;
            }

            var journalStorage = new RecordingJournalStorageProvider(
                new VolatileJournalStorageProvider());
            var reminderTable = new VolatileReminderTable();
            var clock = new ScenarioClock();
            var builder = new InProcessTestClusterBuilder(SiloCount);
            var handlerAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => SynapseWiring.TryGetManifest(assembly, out _))
                .ToArray();

            builder.ConfigureSilo((options, silo) =>
            {
                silo.AddDigitalBrain(LabelOf(options.SiloName));
                silo.Services.AddSerializer(serializer => serializer.AddJsonSerializer(IsAdditionalJsonType));

                foreach (var assembly in handlerAssemblies)
                {
                    silo.AddBroadcastHandlers(assembly);
                }

                silo.UseInMemoryReminderService();
                silo.Services.AddGrainService<SpoofReminderService>();
                silo.Services.AddSingleton<ISpoofReminderServiceClient, SpoofReminderServiceClient>();
                silo.Services.AddSingleton<IReminderTable>(reminderTable);
                silo.Services.Configure<ReminderOptions>(reminders =>
                {
                    reminders.InitializationTimeout = TimeSpan.FromSeconds(1);
                    reminders.MinimumReminderPeriod = TimeSpan.FromMilliseconds(50);
                    reminders.RefreshReminderListPeriod = TimeSpan.FromMilliseconds(50);
                });
                silo.Services.AddSingleton<IJournalStorageProvider>(journalStorage);
                silo.Services.AddSingleton<TimeProvider>(clock);
            });
            builder.ConfigureClient(client =>
            {
                client.Services.AddSerializer(serializer => serializer.AddJsonSerializer(IsAdditionalJsonType));
            });

            var cluster = builder.Build();
            await cluster.DeployAsync();

            _observer = new SynapseObserver();
            _journalStorage = journalStorage;
            _clock = clock;
            _cluster = cluster;
        }
        finally
        {
            StartGate.Release();
        }
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
        await WaitForClientConnectivityAsync(cluster.Client);
    }

    public static async Task StopAsync()
    {
        await StartGate.WaitAsync();
        try
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
            _journalStorage = null;
            _clock = null;
        }
        finally
        {
            StartGate.Release();
        }
    }

    public static string LabelOf(string siloName)
        => LabelOfInstance(short.Parse(
            siloName.AsSpan(siloName.LastIndexOf('_') + 1),
            System.Globalization.CultureInfo.InvariantCulture));

    public static void AddJsonSerializer(Func<Type, bool> isSupported)
    {
        ArgumentNullException.ThrowIfNull(isSupported);

        if (_cluster is not null)
        {
            throw new InvalidOperationException("JSON serializers must be registered before the simulation cluster starts.");
        }

        JsonSerializerPredicates.Add(isSupported);
    }

    private static string LabelOfInstance(short instance) => SiloLabels[instance % SiloLabels.Length];

    private static bool IsAdditionalJsonType(Type type)
        => JsonSerializerPredicates.Any(isSupported => isSupported(type));

    private static InProcessTestCluster Deployed() => _cluster
        ?? throw new InvalidOperationException(
            $"The simulation cluster is not running. Call {nameof(SimulationCluster)}.{nameof(SimulationCluster.StartAsync)} before a scenario runs.");

    private static RecordingJournalStorageProvider JournalStorage()
        => _journalStorage
            ?? throw new InvalidOperationException(
                $"The simulation cluster is not running. Call {nameof(SimulationCluster)}.{nameof(SimulationCluster.StartAsync)} before a scenario runs.");

    private static async Task WaitForClientConnectivityAsync(IClusterClient client)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
        OrleansException? lastFailure = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                await client.GetGrain<IManagementGrain>(0).GetHosts();
                return;
            }
            catch (OrleansException failure)
            {
                lastFailure = failure;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        throw new TimeoutException("The simulation client did not reconnect after its silo restarted.", lastFailure);
    }
}
