using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;

namespace DigitalBrain.Testing;

internal sealed class ComposedFixture : IAsyncDisposable
{
    private static readonly DateTimeOffset FixedEpoch = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan DeactivationBound = TimeSpan.FromSeconds(30);

    private readonly Action<DigitalBrainTestBuilder> compose;
    private readonly RecordingJournalStorageProvider journalStorage = new(new VolatileJournalStorageProvider());
    private readonly ControllableTimeProvider timeProvider = new(FixedEpoch);
    private readonly Lazy<Task> boot;
#pragma warning disable CA2213 // Disposed through the Interlocked.Exchange in DisposeAsync.
    private InProcessTestCluster? cluster;
#pragma warning restore CA2213

    internal ComposedFixture(Action<DigitalBrainTestBuilder> compose)
    {
        this.compose = compose;
        Fingerprint = FingerprintOf(compose);
        Clock = new TestClock(timeProvider);
        boot = new(BootAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal string Fingerprint { get; }

    internal bool HasBooted => boot.IsValueCreated;

    internal TestClock Clock { get; }

    internal Brain Brain
    {
        get => field ?? throw new InvalidOperationException("The composed DigitalBrain cluster is not running.");
        private set;
    }

    internal static string FingerprintOf(Action<DigitalBrainTestBuilder> compose)
    {
        var builder = new DigitalBrainTestBuilder();
        compose(builder);
        return builder.Seal().Fingerprint();
    }

    internal async Task<ComposedFixture> LeaseAsync(CancellationToken cancellationToken)
    {
        await boot.Value.WaitAsync(cancellationToken);
        return this;
    }

    internal JournalFaultRegistration ArmFault(
        NeuronId target, string message, int allowCommitsBeforeFault, bool stickyUntilDisarm)
        => journalStorage.ArmFault(target, message, allowCommitsBeforeFault, stickyUntilDisarm);

    internal bool DisarmFault(JournalFaultRegistration registration)
        => journalStorage.DisarmFault(registration);

    internal IReadOnlyList<string> UnconsumedFaults() => journalStorage.UnconsumedFaults();

    internal async Task DeactivateAsync(IReadOnlyList<NeuronId> neurons, CancellationToken cancellationToken)
    {
        var running = cluster
            ?? throw new InvalidOperationException("The composed DigitalBrain cluster is not running.");
        var management = running.Client.GetGrain<IManagementGrain>(0);
        var addresses = neurons.Select(neuron => GrainId.Create(neuron.Kind, neuron.Name)).ToArray();

        var bound = DateTime.UtcNow + DeactivationBound;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await management.ForceActivationCollection(TimeSpan.Zero).WaitAsync(cancellationToken);
            var statistics = await management.GetDetailedGrainStatistics().WaitAsync(cancellationToken);
            var alive = statistics
                .Where(statistic => addresses.Contains(statistic.GrainId))
                .Select(statistic => statistic.GrainId.ToString())
                .ToArray();
            if (alive.Length == 0)
            {
                return;
            }

            if (DateTime.UtcNow > bound)
            {
                throw new TimeoutException(
                    $"Activations survived forced collection for {DeactivationBound.TotalSeconds:F0}s: "
                    + string.Join(", ", alive));
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A failed boot is reported to the leasing test; teardown only needs it settled.")]
    internal async Task SettleAsync()
    {
        if (!boot.IsValueCreated)
        {
            return;
        }

        try
        {
            await boot.Value;
        }
        catch (Exception)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        var running = Interlocked.Exchange(ref cluster, null);
        if (running is null)
        {
            return;
        }

        try
        {
            await running.StopAllSilosAsync();
        }
        finally
        {
            await running.DisposeAsync();
        }
    }

    private async Task BootAsync()
    {
        var builder = new DigitalBrainTestBuilder();
        compose(builder);
        var composition = builder.Seal();

        var clusterBuilder = new InProcessTestClusterBuilder(1);
        clusterBuilder.ConfigureSilo((options, silo) =>
        {
            silo.Services.AddSingleton<IJournalStorageProvider>(journalStorage);
            foreach (var (contract, instance) in composition.Services)
            {
                silo.Services.AddSingleton(contract, instance);
            }

            silo.UseInMemoryReminderService();
            silo.AddDigitalBrain(composition.ModuleTypes);
        });
        clusterBuilder.ConfigureClient(client => client.Services.AddDigitalBrainWireCodec(composition.ModuleTypes));

        var deployed = clusterBuilder.Build();
        try
        {
            await deployed.DeployAsync();
        }
        catch
        {
            await deployed.DisposeAsync();
            throw;
        }

        cluster = deployed;
        Brain = new Brain(deployed.Client);
    }
}
