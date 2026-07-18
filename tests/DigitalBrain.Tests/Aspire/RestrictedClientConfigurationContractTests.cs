using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.DigitalBrain;
using DigitalBrain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Tests.Aspire;

public sealed class RestrictedClientConfigurationContractTests
{
    [Fact]
    public async Task Restricted_projection_matches_the_runtime_client_configuration_contract()
    {
        var builder = AspireModelTestBuilder.Create();
        var brain = CompleteBrain(builder);
        var client = builder.AddContainer("client", "scratch")
            .WithReference(brain.AsClient());

        var result = await ExecutionConfigurationBuilder.Create(client.Resource)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(
                new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
                NullLogger.Instance,
                CancellationToken.None);
        var environment = result.EnvironmentVariables.ToDictionary(
            pair => pair.Key,
            pair => pair.Value?.ToString(),
            StringComparer.Ordinal);

        Assert.Equal("brain-cluster", environment["Orleans__ClusterId"]);
        Assert.Equal("brain-service", environment["Orleans__ServiceId"]);
        Assert.Equal("AzureTableStorage", environment["Orleans__Clustering__ProviderType"]);
        Assert.Equal("brain-clustering", environment["Orleans__Clustering__ServiceKey"]);
        Assert.Equal("brain", environment["DigitalBrain__Client__Name"]);
        Assert.Equal("1", environment["DigitalBrain__Client__ContractVersion"]);
        Assert.Contains("ConnectionStrings__brain-clustering", environment.Keys);
    }

    private static IResourceBuilder<DigitalBrainResource> CompleteBrain(
        IDistributedApplicationBuilder builder) =>
        builder.AddDigitalBrain("brain")
            .WithLLM<GptFast>().AsFast()
            .WithLLM<ClaudeBalanced>().AsBalanced()
            .WithLLM<GptReasoning>().AsReasoning()
            .WithEmbedding<TextEmbedding>();
}
