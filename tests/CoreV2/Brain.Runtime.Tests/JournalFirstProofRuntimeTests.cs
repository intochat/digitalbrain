using Brain.Abstractions.Graph;
using Brain.Abstractions.Journal;
using Brain.Abstractions.Runtime;
using Brain.Core.Journaling;
using Brain.Core.Runtime;
using Brain.Modules.Proof;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Journaling.Json;
using Orleans.TestingHost;
using Xunit;

namespace Brain.Runtime.Tests;

public sealed class JournalFirstProofRuntimeTests
{
    [Fact]
    public async Task Proof_operations_execute_through_durable_neurons_journal_and_live_BrainGraph()
    {
        await using var cluster = await StartClusterAsync();
        var runtime = cluster.Client.GetGrain<IBrainRuntimeGrain>("brain");

        var wire = await runtime.InvokeAsync(new BrainOperationInvocation(
            "Proof.Wire@1",
            "{\"target\":\"assessment\"}",
            "workspace-a",
            "principal-a",
            "wire-1"));
        var run = await runtime.InvokeAsync(new BrainOperationInvocation(
            "Proof.Run@1",
            "{\"value\":\"journal-live\"}",
            "workspace-a",
            "principal-a",
            "run-1"));

        var activity = await runtime.GetActivityAsync(run.ActivityId, "workspace-a");
        var journal = await cluster.Client
            .GetGrain<IBrainActivityGrain>($"workspace-a/{run.ActivityId:n}")
            .ReadJournalAsync("workspace-a", 0, 100);
        var graph = await cluster.Client
            .GetGrain<IBrainGraphGrain>("workspace-a")
            .SnapshotAsync("workspace-a");

        Assert.NotNull(activity);
        Assert.Equal(Brain.Abstractions.Activities.ActivityStatus.Completed, activity.Status);
        Assert.Equal("{\"route\":\"assessment\"}", activity.ResultJson);
        Assert.Contains(journal.Records, record => record.Direction == BrainJournalDirection.Operation);
        Assert.Contains(journal.Records, record => record.Direction == BrainJournalDirection.Inbound);
        Assert.Contains(journal.Records, record => record.Direction == BrainJournalDirection.Outbound);
        Assert.Contains(journal.Records, record => record.Direction == BrainJournalDirection.Delivery);
        Assert.Contains(journal.Records, record => record.ContractId == "ProofProduced@1");
        Assert.Equal(1, Assert.Single(graph.Synapses).UsageCount);
        Assert.NotEqual(wire.ActivityId, run.ActivityId);
    }

    private static async Task<InProcessTestCluster> StartClusterAsync()
    {
        var builder = new InProcessTestClusterBuilder(1);
        builder.ConfigureSilo((_, silo) =>
        {
#pragma warning disable ORLEANSEXP005
            silo.AddJournalStorage().UseJsonJournalFormat(CoreJournalJsonContext.Default);
            silo.ConfigureServices(services =>
            {
                services.AddSingleton<IJournalStorageProvider, VolatileJournalStorageProvider>();
                services.AddSingleton<IBrainOperationHandler, ProofWireOperationHandler>();
                services.AddSingleton<IBrainOperationHandler, ProofRunOperationHandler>();
            });
#pragma warning restore ORLEANSEXP005
        });
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }
}
