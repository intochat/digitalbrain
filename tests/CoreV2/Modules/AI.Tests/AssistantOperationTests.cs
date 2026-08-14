using Brain.Abstractions.Graph;
using Brain.Abstractions.Journal;
using Brain.Abstractions.Runtime;
using Brain.Core.Journaling;
using Brain.Core.Runtime;
using Brain.Modules.AI;
using Brain.Modules.Proof;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Journaling.Json;
using Orleans.TestingHost;
using Xunit;

namespace Brain.Modules.AI.Tests;

public sealed class AssistantOperationTests
{
    [Fact]
    public async Task Assistant_uses_operations_and_keeps_tool_flow_in_one_activity_journal()
    {
        await using var cluster = await StartClusterAsync();
        var runtime = cluster.Client.GetGrain<IBrainRuntimeGrain>("brain");

        var receipt = await runtime.InvokeAsync(new BrainOperationInvocation(
            "Assistant.Chat@1",
            "{\"message\":\"Wire proof to assessment and run value journal-live\"}",
            "workspace-a",
            "principal-a",
            "chat-1"));
        var activity = await runtime.GetActivityAsync(receipt.ActivityId, "workspace-a");
        var journal = await cluster.Client
            .GetGrain<IBrainActivityGrain>($"workspace-a/{receipt.ActivityId:n}")
            .ReadJournalAsync("workspace-a", 0, 100);
        var graph = await cluster.Client
            .GetGrain<IBrainGraphGrain>("workspace-a")
            .SnapshotAsync("workspace-a");

        Assert.NotNull(activity);
        Assert.Contains("Proof.Wire@1", activity.ResultJson, StringComparison.Ordinal);
        Assert.Contains(journal.Records, record => record.ContractId == "Assistant.ToolSelected@1");
        Assert.Contains(journal.Records, record => record.ContractId == "Proof.Wire@1");
        Assert.Contains(journal.Records, record => record.ContractId == "Proof.Run@1");
        Assert.Contains(journal.Records, record => record.ContractId == "ProofProduced@1");
        Assert.Equal(1, Assert.Single(graph.Synapses).UsageCount);
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
                services.AddSingleton<IAssistantChatModel, DeterministicAssistantModel>();
                services.AddSingleton<IBrainOperationHandler, AssistantChatOperationHandler>();
                services.AddSingleton<IBrainOperationHandler, ProofWireOperationHandler>();
                services.AddSingleton<IBrainOperationHandler, ProofRunOperationHandler>();
            });
#pragma warning restore ORLEANSEXP005
        });
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }

    private sealed class DeterministicAssistantModel : IAssistantChatModel
    {
        public Task<AssistantModelPlan> PlanAsync(
            string message,
            IReadOnlyList<BrainOperationDescriptor> operations,
            CancellationToken cancellationToken)
            => Task.FromResult(new AssistantModelPlan(
                [
                    new AssistantToolCall("Proof.Wire@1", "{\"target\":\"assessment\"}"),
                    new AssistantToolCall("Proof.Run@1", "{\"value\":\"journal-live\"}"),
                ],
                "Proof is wired and ran through assessment."));
    }
}
