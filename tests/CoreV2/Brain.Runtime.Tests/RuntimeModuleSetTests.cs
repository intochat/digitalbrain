using Brain.Modules.Behavior;
using Brain.Modules.Conversation;
using Brain.Modules.Proof;
using Brain.Modules.Scheduling;
using Brain.Runtime.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.TestingHost;
using Xunit;

namespace Brain.Runtime.Tests;

public sealed class RuntimeModuleSetTests
{
    [Fact]
    public async Task Launch_module_set_is_explicit_ordered_and_reports_optional_setup()
    {
        await using var cluster = await StartClusterAsync();
        var modules = await cluster.Client.GetGrain<IProductRuntimeGrain>("product").GetModulesAsync();

        Assert.Equal(
            ["proof", "conversation", "scheduling", "behavior", "ai", "memory", "google", "salesforce"],
            modules.Select(static module => module.Id));
        Assert.All(modules.Take(4), module => Assert.Equal(RuntimeModuleStatus.Ready, module.Status));
        Assert.All(modules.Skip(4), module =>
        {
            Assert.Equal(RuntimeModuleStatus.NeedsSetup, module.Status);
            Assert.False(string.IsNullOrWhiteSpace(module.SetupMessage));
        });
    }

    private static async Task<InProcessTestCluster> StartClusterAsync()
    {
        var builder = new InProcessTestClusterBuilder(1);
        builder.ConfigureSilo((_, silo) =>
        {
            silo.AddMemoryGrainStorage("Default");
            silo.UseInMemoryReminderService();
            silo.ConfigureServices(services =>
            {
                services.AddSingleton<IRuntimeProductModule, ProofProductModule>();
                services.AddSingleton<IRuntimeProductModule, ConversationProductModule>();
                services.AddSingleton<IRuntimeProductModule, SchedulingProductModule>();
                services.AddSingleton<IRuntimeProductModule, BehaviorProductModule>();
                services.AddSingleton<IRuntimeProductModule>(new SetupRequiredProductModule(
                    "ai", "AI", "Configure a local or hosted model provider."));
                services.AddSingleton<IRuntimeProductModule>(new SetupRequiredProductModule(
                    "memory", "Memory", "Configure a workspace memory provider."));
                services.AddSingleton<IRuntimeProductModule>(new SetupRequiredProductModule(
                    "google", "Google", "Configure the Google MCP connection."));
                services.AddSingleton<IRuntimeProductModule>(new SetupRequiredProductModule(
                    "salesforce", "Salesforce", "Configure the Salesforce MCP connection."));
            });
        });
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }
}
