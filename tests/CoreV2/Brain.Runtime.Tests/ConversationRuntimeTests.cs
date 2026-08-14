using System.Text.Json;
using Brain.Modules.Conversation;
using Brain.Runtime.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.TestingHost;
using Xunit;

namespace Brain.Runtime.Tests;

public sealed class ConversationRuntimeTests
{
    [Fact]
    public async Task Conversation_messages_are_durable_ordered_and_workspace_scoped()
    {
        await using var cluster = await StartClusterAsync();
        var runtime = cluster.Client.GetGrain<IProductRuntimeGrain>("product");

        var modules = await runtime.GetModulesAsync();
        var operations = await runtime.GetOperationsAsync();
        await InvokeAsync(runtime, "workspace-a", "message-1", "Hello");
        await InvokeAsync(runtime, "workspace-a", "message-2", "Again");
        var workspaceA = await ReadAsync(runtime, cluster, "workspace-a", "read-a");
        var workspaceB = await ReadAsync(runtime, cluster, "workspace-b", "read-b");

        Assert.Contains(modules, module => module.Id == "conversation" && module.Status == RuntimeModuleStatus.Ready);
        Assert.Contains(operations, operation => operation.Id == "conversation/send@1");
        Assert.Equal(["Hello", "Again"], Messages(workspaceA));
        Assert.Empty(Messages(workspaceB));
    }

    private static async Task InvokeAsync(
        IProductRuntimeGrain runtime,
        string workspace,
        string request,
        string message)
        => _ = await runtime.InvokeAsync(new RuntimeInvocation(
            "conversation/send@1",
            JsonSerializer.Serialize(new { conversationId = "main", message }),
            workspace,
            "owner",
            request));

    private static async Task<string> ReadAsync(
        IProductRuntimeGrain runtime,
        InProcessTestCluster cluster,
        string workspace,
        string request)
    {
        var receipt = await runtime.InvokeAsync(new RuntimeInvocation(
            "conversation/read@1",
            "{\"conversationId\":\"main\"}",
            workspace,
            "owner",
            request));
        var activity = await cluster.Client.GetGrain<IProductActivityGrain>(receipt.Activity).GetAsync(workspace);
        Assert.NotNull(activity);
        Assert.Equal(RuntimeActivityStatus.Completed, activity.Status);
        return Assert.IsType<string>(activity.ResultJson);
    }

    private static string[] Messages(string snapshotJson)
    {
        using var document = JsonDocument.Parse(snapshotJson);
        return document.RootElement.GetProperty("messages")
            .EnumerateArray()
            .Select(message => message.GetProperty("text").GetString()!)
            .ToArray();
    }

    private static async Task<InProcessTestCluster> StartClusterAsync()
    {
        var builder = new InProcessTestClusterBuilder(1);
        builder.ConfigureSilo((_, silo) =>
        {
            silo.AddMemoryGrainStorage("Default");
            silo.ConfigureServices(services =>
                services.AddSingleton<IRuntimeProductModule, ConversationProductModule>());
        });
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }
}
