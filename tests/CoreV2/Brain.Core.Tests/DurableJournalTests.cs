using Brain.Abstractions.Journal;
using Brain.Abstractions.Runtime;
using Brain.Core.Journaling;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Journaling.Json;
using Orleans.TestingHost;
using Xunit;

namespace Brain.Core.Tests;

public sealed class DurableJournalTests
{
    [Fact]
    public async Task Activity_journal_assigns_order_deduplicates_and_filters_workspace()
    {
        await using var cluster = await StartClusterAsync();
        var activity = Guid.NewGuid();
        var grain = cluster.Client.GetGrain<IBrainActivityGrain>($"workspace-a/{activity:n}");
        var invocation = new BrainOperationInvocation(
            "Proof.Run@1",
            "{\"value\":\"journal-live\"}",
            "workspace-a",
            "principal-a",
            "journal-test");
        await grain.StartAsync(activity, invocation);
        var write = Write(activity);

        var first = await grain.AppendAsync(write);
        var duplicate = await grain.AppendAsync(write);
        var second = await grain.AppendAsync(Write(activity, Guid.NewGuid()));
        var page = await grain.ReadJournalAsync("workspace-a", 0, 10);

        Assert.Equal(first, duplicate);
        Assert.Equal([1L, 2L], page.Records.Select(record => record.Sequence));
        Assert.Equal(2, second.Sequence);
        Assert.Null(await grain.GetAsync("workspace-b"));
        Assert.Empty((await grain.ReadJournalAsync("workspace-b", 0, 10)).Records);
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

    private static BrainJournalWrite Write(Guid activity, Guid? recordId = null)
        => new(
            recordId ?? Guid.NewGuid(),
            "workspace-a",
            activity,
            "principal-a",
            "proof/source/workspace",
            BrainJournalDirection.Outbound,
            "ProofProduced@1",
            Guid.NewGuid(),
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            1,
            "emitted",
            "Proof produced");
}
