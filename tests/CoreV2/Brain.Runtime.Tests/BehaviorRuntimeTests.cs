using System.Text.Json;
using Brain.Modules.Behavior;
using Brain.Runtime.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.TestingHost;
using Xunit;

namespace Brain.Runtime.Tests;

public sealed class BehaviorRuntimeTests
{
    [Fact]
    public async Task Behavior_revisions_activate_and_run_durably_per_workspace()
    {
        await using var cluster = await StartClusterAsync();
        var runtime = cluster.Client.GetGrain<IProductRuntimeGrain>("product");

        var published = await InvokeAsync(
            runtime,
            cluster,
            "workspace-a",
            "publish-1",
            "behavior/publish@1",
            JsonSerializer.Serialize(new
            {
                behaviorId = "welcome",
                name = "Welcome",
                source = "{\"template\":\"Hello {{input}}\"}",
            }));
        var activated = await InvokeAsync(
            runtime,
            cluster,
            "workspace-a",
            "activate-1",
            "behavior/activate@1",
            "{\"behaviorId\":\"welcome\",\"revision\":1}");
        var run = await InvokeAsync(
            runtime,
            cluster,
            "workspace-a",
            "run-1",
            "behavior/run@1",
            "{\"behaviorId\":\"welcome\",\"input\":\"World\"}");
        var otherWorkspace = await InvokeAsync(
            runtime,
            cluster,
            "workspace-b",
            "read-b",
            "behavior/read@1",
            "{\"behaviorId\":\"welcome\"}");

        Assert.Equal(1, Number(published, "latestRevision"));
        Assert.Equal(1, Number(activated, "activeRevision"));
        Assert.Equal("Hello World", LastRunOutput(run));
        Assert.Equal("missing", String(otherWorkspace, "status"));
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

    private static int Number(string json, string name)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(name).GetInt32();
    }

    private static string String(string json, string name)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(name).GetString()!;
    }

    private static string LastRunOutput(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("runs")[0].GetProperty("output").GetString()!;
    }

    private static async Task<InProcessTestCluster> StartClusterAsync()
    {
        var builder = new InProcessTestClusterBuilder(1);
        builder.ConfigureSilo((_, silo) =>
        {
            silo.AddMemoryGrainStorage("Default");
            silo.ConfigureServices(services =>
                services.AddSingleton<IRuntimeProductModule, BehaviorProductModule>());
        });
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }
}
