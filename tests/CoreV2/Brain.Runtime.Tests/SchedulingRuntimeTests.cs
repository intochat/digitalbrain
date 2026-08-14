using System.Text.Json;
using Brain.Modules.Scheduling;
using Brain.Runtime.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.TestingHost;
using Xunit;

namespace Brain.Runtime.Tests;

public sealed class SchedulingRuntimeTests
{
    [Fact]
    public async Task Schedule_is_reminder_backed_durable_and_workspace_scoped()
    {
        await using var cluster = await StartClusterAsync();
        var runtime = cluster.Client.GetGrain<IProductRuntimeGrain>("product");
        var dueAt = DateTimeOffset.UtcNow.AddHours(1);

        var scheduled = await InvokeAsync(
            runtime,
            cluster,
            "workspace-a",
            "schedule-1",
            "scheduling/schedule@1",
            JsonSerializer.Serialize(new { scheduleId = "daily", title = "Review", dueAtUtc = dueAt }));
        var readA = await InvokeAsync(
            runtime,
            cluster,
            "workspace-a",
            "read-a",
            "scheduling/read@1",
            "{\"scheduleId\":\"daily\"}");
        var readB = await InvokeAsync(
            runtime,
            cluster,
            "workspace-b",
            "read-b",
            "scheduling/read@1",
            "{\"scheduleId\":\"daily\"}");

        Assert.Equal("scheduled", Property(scheduled, "status"));
        Assert.Equal("Review", Property(readA, "title"));
        Assert.Equal("missing", Property(readB, "status"));
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

    private static string Property(string json, string name)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(name).GetString()!;
    }

    private static async Task<InProcessTestCluster> StartClusterAsync()
    {
        var builder = new InProcessTestClusterBuilder(1);
        builder.ConfigureSilo((_, silo) =>
        {
            silo.AddMemoryGrainStorage("Default");
            silo.UseInMemoryReminderService();
            silo.ConfigureServices(services =>
                services.AddSingleton<IRuntimeProductModule, SchedulingProductModule>());
        });
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }
}
