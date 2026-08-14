using System.Text.Json;
using Brain.Modules.Memory;
using Brain.Runtime.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.TestingHost;
using Xunit;

namespace Brain.Runtime.Tests;

public sealed class MemoryRuntimeTests
{
    [Fact]
    public async Task Memory_records_are_durable_searchable_and_workspace_scoped()
    {
        await using var cluster = await StartClusterAsync();
        var runtime = cluster.Client.GetGrain<IProductRuntimeGrain>("product");

        await InvokeAsync(
            runtime,
            cluster,
            "workspace-a",
            "store-1",
            "memory/store@1",
            "{\"namespace\":\"notes\",\"key\":\"alpha\",\"text\":\"durable brain note\"}");
        var workspaceA = await InvokeAsync(
            runtime,
            cluster,
            "workspace-a",
            "search-a",
            "memory/search@1",
            "{\"namespace\":\"notes\",\"query\":\"brain\"}");
        var workspaceB = await InvokeAsync(
            runtime,
            cluster,
            "workspace-b",
            "search-b",
            "memory/search@1",
            "{\"namespace\":\"notes\",\"query\":\"brain\"}");

        Assert.Equal(["alpha"], Keys(workspaceA));
        Assert.Empty(Keys(workspaceB));
    }

    private static async Task<string> InvokeAsync(
        IProductRuntimeGrain runtime,
        InProcessTestCluster cluster,
        string workspace,
        string request,
        string operation,
        string input)
    {
        var receipt = await runtime.InvokeAsync(new RuntimeInvocation(
            operation,
            input,
            workspace,
            "owner",
            request));
        var activity = await cluster.Client.GetGrain<IProductActivityGrain>(receipt.Activity).GetAsync(workspace);
        Assert.NotNull(activity);
        Assert.Equal(RuntimeActivityStatus.Completed, activity.Status);
        return Assert.IsType<string>(activity.ResultJson);
    }

    private static string[] Keys(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("records")
            .EnumerateArray()
            .Select(record => record.GetProperty("key").GetString()!)
            .ToArray();
    }

    private static async Task<InProcessTestCluster> StartClusterAsync()
    {
        var builder = new InProcessTestClusterBuilder(1);
        builder.ConfigureSilo((_, silo) =>
        {
            silo.AddMemoryGrainStorage("Default");
            silo.ConfigureServices(services =>
                services.AddSingleton<IRuntimeProductModule, MemoryProductModule>());
        });
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }
}
