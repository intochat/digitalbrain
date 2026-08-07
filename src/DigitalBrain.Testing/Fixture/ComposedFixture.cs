using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;

namespace DigitalBrain.Testing;

internal sealed class ComposedFixture : IAsyncDisposable
{
    private static readonly DateTimeOffset FixedEpoch = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan DeactivationBound = TimeSpan.FromSeconds(30);
    private static readonly ScopeKey DefaultScope = new("testing/default");

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

    internal WorkspaceChannel DefaultWorkspace
    {
        get => field ?? throw new InvalidOperationException("The composed DigitalBrain cluster is not running.");
        private set;
    }

    private OrleansWorkspaceChannelIssuer Issuer
    {
        get => field ?? throw new InvalidOperationException("The composed DigitalBrain cluster is not running.");
        set;
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
        NeuronId target, string message, int allowRecordingsBeforeFault, bool stickyUntilDisarm)
        => journalStorage.ArmFault(
            new ScopedNeuronAddress(DefaultScope, target),
            message,
            allowRecordingsBeforeFault,
            stickyUntilDisarm);

    internal bool DisarmFault(JournalFaultRegistration registration)
        => journalStorage.DisarmFault(registration);

    internal IReadOnlyList<string> UnconsumedFaults() => journalStorage.UnconsumedFaults();

    internal WorkspaceChannel OpenWorkspace(
        string scope,
        string source,
        params Type[] permittedIngressSynapses)
        => Issuer.Open(
            new ScopeKey(scope),
            new SynapseSource(source),
            permittedIngressSynapses.ToHashSet());

    internal WorkspaceChannel OpenDefaultWorkspace(
        string source,
        params Type[] permittedIngressSynapses)
        => Issuer.Open(
            DefaultScope,
            new SynapseSource(source),
            permittedIngressSynapses.ToHashSet());

    internal bool HasAmbientAccessServices()
    {
        var running = cluster
            ?? throw new InvalidOperationException("The composed DigitalBrain cluster is not running.");
        var services = running.Client.ServiceProvider;
        return services.GetService<SynapsePublisher>() is not null
            || services.GetService<JournalReader>() is not null
            || services.GetService<OrleansWorkspaceChannelIssuer>() is not null;
    }

    internal Task DeactivateAsync(IReadOnlyList<NeuronId> neurons, CancellationToken cancellationToken)
        => DeactivateAsync(DefaultScope, neurons, cancellationToken);

    internal async Task DeactivateAsync(
        string workspaceScope,
        IReadOnlyList<NeuronId> neurons,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceScope);
        await DeactivateAsync(new ScopeKey(workspaceScope), neurons, cancellationToken);
    }

    private async Task DeactivateAsync(
        ScopeKey scope,
        IReadOnlyList<NeuronId> neurons,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(neurons);
        var running = cluster
            ?? throw new InvalidOperationException("The composed DigitalBrain cluster is not running.");
        var management = running.Client.GetGrain<IManagementGrain>(0);
        var addresses = neurons
            .Select(neuron => NeuronHost.AddressOf(new ScopedNeuronAddress(scope, neuron)))
            .ToArray();

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

    internal Task DrainAsync(NeuronId neuron, CancellationToken cancellationToken)
        => DrainAsync(DefaultScope, neuron, cancellationToken);

    internal Task DrainAsync(string workspaceScope, NeuronId neuron, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceScope);
        return DrainAsync(new ScopeKey(workspaceScope), neuron, cancellationToken);
    }

    private Task DrainAsync(ScopeKey scope, NeuronId neuron, CancellationToken cancellationToken)
    {
        var running = cluster
            ?? throw new InvalidOperationException("The composed DigitalBrain cluster is not running.");
        return running.Client
            .GetGrain<INeuronHost>(NeuronHost.AddressOf(new ScopedNeuronAddress(scope, neuron)))
            .DrainAsync()
            .WaitAsync(cancellationToken);
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
            silo.AddDigitalBrain(composition.Configure, timeProvider);
        });
        clusterBuilder.ConfigureClient(client => client.Services.AddDigitalBrainSerialization(composition.Configure));

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
        Issuer = new OrleansWorkspaceChannelIssuer(
            deployed.Client.ServiceProvider.GetRequiredService<IGrainFactory>());
        DefaultWorkspace = Issuer.Open(
            DefaultScope,
            new SynapseSource("testing/default"),
            new HashSet<Type>());
    }
}
