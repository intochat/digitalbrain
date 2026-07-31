using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Journaling;
using Orleans.TestingHost;

namespace DigitalBrain.Testing;

internal sealed class FixtureCluster : IAsyncDisposable
{
    private const int SiloCount = 3;
    private static readonly TimeSpan SaturatedMachineResponseTimeout = TimeSpan.FromSeconds(90);
    private static readonly DateTimeOffset FixedEpoch =
        new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly RecordingJournalStorageProvider _journalStorage =
        new(new VolatileJournalStorageProvider());
    private readonly IReadOnlyCollection<ICompiledModule> _modules;
    private readonly IReadOnlyDictionary<string, string?> _configuration;
    private readonly VolatileReminderTable _reminderTable = new();
    private readonly ControllableTimeProvider _clock = new(FixedEpoch);
    private readonly TestEdgeRegistry _edges;
    private readonly TimeSpan? _responseTimeout;
#pragma warning disable CA2213 // The cluster is disposed asynchronously in DisposeAsync.
    private InProcessTestCluster? _cluster;
#pragma warning restore CA2213

    private FixtureCluster(TestFixtureComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        _modules = composition.Modules.ToArray();
        _configuration = composition.Configuration;
        _edges = composition.Edges;
        _responseTimeout = composition.ResponseTimeout;
        _edges.AttachTimeProvider(_clock, _clock.Reset);
    }

    internal static async Task<FixtureCluster> StartAsync(TestFixtureComposition composition)
    {
        var fixture = new FixtureCluster(composition);

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

    internal IServiceProvider ClientServices
        => (_cluster?.Client as IClusterClient)?.ServiceProvider
            ?? throw new InvalidOperationException(
                "The DigitalBrain fixture cluster client services are not available.");

    internal TestEdgeRegistry Edges => _edges;

    internal JournalFaultRegistration ArmJournalFault(
        NeuronId target,
        string message,
        int allowCommitsBeforeFault = 0)
        => _journalStorage.ArmFault(target, message, allowCommitsBeforeFault);

    internal bool DisarmJournalFault(JournalFaultRegistration registration)
        => _journalStorage.DisarmFault(registration);

    internal async Task<bool> HasOutboxWakeupAsync(NeuronId neuron)
    {
        var grainId = GrainId.Create(OutboxWakeup.GrainTypeName, neuron.ToString());
        var entry = await _reminderTable.ReadRow(grainId, OutboxWakeup.ReminderName);
        return entry is not null;
    }

    internal async Task<(TestClock Clock, long EdgeGeneration)> PrepareMethodAsync(string scope, BrainTestDiagnostics diagnostics)
    {
        var edgeGeneration = _edges.ResetMethodScope();
        await _reminderTable.TestOnlyClearTable();
        return (
            new TestClock(_clock, new TestReminderDriver(_reminderTable, Client, scope), diagnostics),
            edgeGeneration);
    }

    internal async Task RestartHostAsync(NeuronId neuron, CancellationToken cancellationToken)
    {
        var cluster = _cluster
            ?? throw new InvalidOperationException(
                "The DigitalBrain fixture cluster is not running.");
        var management = cluster.Client.GetGrain<IManagementGrain>(0);
        var statistics = await management
            .GetDetailedGrainStatistics()
            .WaitAsync(cancellationToken);
        var hosting = statistics
            .FirstOrDefault(statistic => statistic.GrainId == neuron.ToGrainId())
            ?.SiloAddress
            ?? throw new InvalidOperationException(
                $"{neuron} is not activated on any silo, so no host can be restarted.");
        var host = cluster.Silos.Single(silo => silo.SiloAddress.Equals(hosting));

        // Restart mutates assembly-scoped topology. Cancellation may prevent it
        // from starting, but cannot detach this method from an in-flight restart
        // and let the fixture lease expose an unstable cluster to the next test.
        cancellationToken.ThrowIfCancellationRequested();
        await cluster.RestartSiloAsync(host);
        await cluster
            .WaitForLivenessToStabilizeAsync(didKill: true)
            .WaitAsync(cancellationToken);
        await cluster
            .WaitForClusterManifestToStabilizeAsync(didKill: true)
            .WaitAsync(cancellationToken);
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

            silo.Configuration["DigitalBrain:Security:StateProtectionKey"] =
                Convert.ToBase64String(new byte[32]);

            foreach (var entry in _configuration)
            {
                silo.Configuration[entry.Key] = entry.Value;
            }

            DigitalBrainRuntime.Add(silo, _modules);
            silo.Services.AddSingleton(new ReminderSourceAllowlist([TestReminderDeliveryService.SourceType]));
            silo.Services.AddGrainService<TestReminderDeliveryService>();
            silo.Services.AddSingleton<
                ITestReminderDeliveryServiceClient,
                TestReminderDeliveryServiceClient>();
            silo.Services.AddSingleton<Orleans.Timers.IReminderRegistry>(
                new TestReminderRegistry(_reminderTable, _clock));
            silo.Services.AddSingleton<IJournalStorageProvider>(_journalStorage);
            _edges.ConfigureServices(silo.Services);

            var siloResponseTimeout = _responseTimeout ?? SaturatedMachineResponseTimeout;
            silo.Services.Configure<SiloMessagingOptions>(
                messaging => messaging.ResponseTimeout = siloResponseTimeout);
        });
        builder.ConfigureClient(client =>
        {
            foreach (var module in _modules)
            {
                module.PrepareSerialization(client.Services);
            }

            var selected = _modules
                .OrderBy(module => module.Id.Value, StringComparer.Ordinal)
                .ToArray();
            var capabilities = ActiveCapabilityCatalog.Create(selected);
            client.Services.AddSingleton(capabilities);
            client.Services.AddSingleton(ActiveModuleContractTypeMap.Create(selected, capabilities));

            var clientResponseTimeout = _responseTimeout ?? SaturatedMachineResponseTimeout;
            client.Services.Configure<ClientMessagingOptions>(
                messaging => messaging.ResponseTimeout = clientResponseTimeout);
        });

        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }
}
