using Brain.Abstractions.Graph;
using Brain.Core.Journaling;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Journaling.Json;
using Orleans.TestingHost;
using Xunit;

namespace Brain.Core.Tests;

public sealed class DurableBrainGraphTests
{
    [Fact]
    public async Task BrainGraph_preserves_revisions_and_exposes_live_usage()
    {
        await using var cluster = await StartClusterAsync();
        var graph = cluster.Client.GetGrain<IBrainGraphGrain>("workspace-a");
        var activity = Guid.NewGuid();
        var installed = await graph.InstallAsync(Change(activity));
        var replaced = await graph.ReplaceAsync(installed.Id, Change(activity, "proof/assessment-v2/workspace"));
        await graph.RecordUsageAsync(installed.Id, "workspace-a", activity);

        var snapshot = await graph.SnapshotAsync("workspace-a");
        var history = await graph.HistoryAsync("workspace-a", installed.Id);

        Assert.Equal(2, replaced.Revision);
        Assert.Equal([1L, 2L], history.Select(revision => revision.Revision));
        Assert.Equal(1, Assert.Single(snapshot.Synapses).UsageCount);
        Assert.Equal(2, snapshot.Neurons.Count);
        Assert.Empty((await graph.SnapshotAsync("workspace-b")).Synapses);
    }

    private static async Task<InProcessTestCluster> StartClusterAsync()
    {
        var builder = new InProcessTestClusterBuilder(1);
        builder.ConfigureSilo((_, silo) =>
        {
#pragma warning disable ORLEANSEXP005
            silo.AddJournalStorage().UseJsonJournalFormat(CoreJournalJsonContext.Default);
            silo.ConfigureServices(services =>
                services.AddSingleton<IJournalStorageProvider, VolatileJournalStorageProvider>());
#pragma warning restore ORLEANSEXP005
        });
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }

    private static BrainSynapseChange Change(Guid activity, string target = "proof/assessment/workspace")
        => new(
            "workspace-a",
            new BrainNeuronView("proof/source/workspace", "proof", "source", "workspace", 0),
            new BrainNeuronView(target, "proof", "assessment", "workspace", 0),
            "ProofProduced@1",
            "ProofProduced@1",
            activity);
}
