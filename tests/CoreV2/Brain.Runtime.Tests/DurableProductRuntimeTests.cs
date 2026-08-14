using Brain.Modules.Proof;
using Brain.Runtime.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.TestingHost;
using Xunit;

namespace Brain.Runtime.Tests;

public sealed class DurableProductRuntimeTests
{
    [Fact]
    public async Task Proof_invocation_is_discoverable_durable_and_idempotent()
    {
        await using var cluster = await StartClusterAsync();
        var runtime = cluster.Client.GetGrain<IProductRuntimeGrain>("product");

        var modules = await runtime.GetModulesAsync();
        var operations = await runtime.GetOperationsAsync();
        var invocation = new RuntimeInvocation(
            "proof/run@1",
            "{\"value\":\"hello\"}",
            "workspace-a",
            "owner",
            "request-1");
        var first = await runtime.InvokeAsync(invocation);
        var repeated = await runtime.InvokeAsync(invocation);
        var activity = await cluster.Client
            .GetGrain<IProductActivityGrain>(first.Activity)
            .GetAsync("workspace-a");

        Assert.Contains(modules, module => module.Id == "proof" && module.Status == RuntimeModuleStatus.Ready);
        Assert.Contains(operations, operation => operation.Id == "proof/run@1");
        Assert.Equal(first, repeated);
        Assert.NotNull(activity);
        Assert.Equal(RuntimeActivityStatus.Completed, activity.Status);
        Assert.Equal(3, activity.Sequence);
        Assert.Equal("{\"route\":\"proof/hello\"}", activity.ResultJson);
        Assert.Null(await cluster.Client
            .GetGrain<IProductActivityGrain>(first.Activity)
            .GetAsync("another-workspace"));
    }

    [Fact]
    public async Task Idempotency_key_reuse_with_different_input_is_rejected()
    {
        await using var cluster = await StartClusterAsync();
        var runtime = cluster.Client.GetGrain<IProductRuntimeGrain>("product");
        var original = new RuntimeInvocation(
            "proof/run@1",
            "{\"value\":\"first\"}",
            "workspace-a",
            "owner",
            "request-1");
        await runtime.InvokeAsync(original);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.InvokeAsync(original with { InputJson = "{\"value\":\"second\"}" }));

        Assert.Contains("cannot be reused", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<InProcessTestCluster> StartClusterAsync()
    {
        var builder = new InProcessTestClusterBuilder(1);
        builder.ConfigureSilo((_, silo) =>
        {
            silo.AddMemoryGrainStorage("Default");
            silo.ConfigureServices(services =>
                services.AddSingleton<IRuntimeProductModule, ProofProductModule>());
        });
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }
}
