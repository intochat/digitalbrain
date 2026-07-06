using System.Collections.Concurrent;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Kernel;
using DigitalBrain.Kernel.SelfEvolution;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Journaling.Json;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.TestSupport;

#pragma warning disable ORLEANSEXP005

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OrleansJournalClusterCollection : ICollectionFixture<OrleansJournalClusterFixture>
{
    public const string Name = "orleans-journal-cluster";
}

public sealed class OrleansJournalClusterFixture : IAsyncLifetime
{
    public InProcessTestCluster Cluster { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);
        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder
                .AddJournalStorage()
                .UseJsonJournalFormat(JournalJson.Configure)
                .ConfigureServices(services =>
                {
                    services.AddScoped<NeuronJournals>();
                    services.AddSingleton<IJournalStorageProvider, VolatileJournalStorageProvider>();
                    services.AddSingleton<ISelfEvolutionApplyHandler, DurableRecordingApplyHandler>();
                });
        });

        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        await Cluster.DisposeAsync();
    }
}

internal sealed class DurableRecordingApplyHandler : ISelfEvolutionApplyHandler
{
    public const string ApplyViaId = "durable.apply";
    private static readonly ConcurrentDictionary<string, int> Applied = new(StringComparer.Ordinal);

    public string ApplyVia => ApplyViaId;
    public SelfEvolutionRisk MaxRisk => SelfEvolutionRisk.KernelRestart;

    public static int Count(string proposalId) => Applied.TryGetValue(proposalId, out var count) ? count : 0;

    public static void Clear() => Applied.Clear();

    public Task<SelfEvolutionApplyResult> ApplyAsync(SelfEvolutionProposal proposal, CancellationToken ct)
    {
        Applied.AddOrUpdate(proposal.ProposalId, 1, (_, count) => count + 1);
        return Task.FromResult(new SelfEvolutionApplyResult(
            proposal.ProposalId,
            proposal.ApplyVia,
            Succeeded: true,
            Details: "applied",
            RollbackCheckpointId: "durable-checkpoint"));
    }
}

#pragma warning restore ORLEANSEXP005

