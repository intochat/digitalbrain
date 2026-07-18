using System.Net;
using Azure.Storage.Blobs;
using DigitalBrain.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using Orleans.TestingHost;
using Testcontainers.Azurite;
using Xunit;

namespace Brain.FeasibilityTests.Journaling;

public sealed class DigitalBrainKernelStartupTests
{
    [Fact]
    public async Task Production_kernel_starts_with_official_azure_durability()
    {
        await using var azurite = new AzuriteBuilder(
                "mcr.microsoft.com/azure-storage/azurite:latest")
            .WithCommand("--skipApiVersionCheck")
            .Build();
        await azurite.StartAsync();
        var connectionString = azurite.GetConnectionString();
        await new BlobServiceClient(connectionString)
            .CreateBlobContainerAsync("brain-journals");
        using var ports = new TestClusterPortAllocator();
        var (siloPort, gatewayPort) =
            ports.AllocateConsecutivePortPairs(1);
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                Args = [],
                EnvironmentName = Environments.Production
            });
        builder.Configuration.AddInMemoryCollection(
            CompleteConfiguration(connectionString));
        builder.AddDigitalBrainKernel("brain");
        builder.UseOrleans(silo => silo.ConfigureEndpoints(
            IPAddress.Loopback,
            siloPort,
            gatewayPort));
        using var host = builder.Build();
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(30));

        await host.StartAsync(timeout.Token);
        await host.StopAsync(timeout.Token);
    }

    private static Dictionary<string, string?> CompleteConfiguration(
        string storage)
    {
        var identity = "kernel-startup-" + Guid.NewGuid().ToString("N");
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Orleans:ClusterId"] = identity,
            ["Orleans:ServiceId"] = identity,
            ["Orleans:Clustering:ProviderType"] = "AzureTableStorage",
            ["Orleans:Clustering:ServiceKey"] = "brain-clustering",
            ["Orleans:Reminders:ProviderType"] = "AzureTableStorage",
            ["Orleans:Reminders:ServiceKey"] = "brain-reminders",
            ["Orleans:GrainStorage:Default:ProviderType"] = "AzureBlobStorage",
            ["Orleans:GrainStorage:Default:ServiceKey"] = "brain-grain-state",
            ["Orleans:Streaming:NeuronNotification:ProviderType"] =
                "AzureQueueStorage",
            ["Orleans:Streaming:NeuronNotification:ServiceKey"] =
                "brain-streams",
            ["DigitalBrain:Storage:Clustering"] = storage,
            ["DigitalBrain:Storage:Reminders"] = storage,
            ["DigitalBrain:Storage:GrainState"] = storage,
            ["DigitalBrain:Storage:Journal"] = storage,
            ["DigitalBrain:Storage:Streams"] = storage,
            ["DigitalBrain:Storage:Outbox"] = storage,
            ["DigitalBrain:AI:OpenAI:ApiKey"] = "test-openai-key",
            ["DigitalBrain:AI:OpenAI:Endpoint"] = "https://openai.test/v1",
            ["DigitalBrain:AI:OpenAI:FastModelId"] = "gpt-5-mini",
            ["DigitalBrain:AI:OpenAI:ReasoningModelId"] = "gpt-5",
            ["DigitalBrain:AI:OpenAI:EmbeddingModelId"] =
                "text-embedding-3-small",
            ["DigitalBrain:AI:Anthropic:ApiKey"] = "test-anthropic-key",
            ["DigitalBrain:AI:Anthropic:Endpoint"] =
                "https://anthropic.test",
            ["DigitalBrain:AI:Anthropic:BalancedModelId"] =
                "claude-sonnet-4-5"
        };
    }

}
