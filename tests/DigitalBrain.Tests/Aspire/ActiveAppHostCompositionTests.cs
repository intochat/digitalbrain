using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.DigitalBrain;
using Aspire.Hosting.OpenAI;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Tests.Aspire;

public sealed class ActiveAppHostCompositionTests
{
    [Fact]
    public async Task Active_AppHost_publish_model_contains_both_brain_projections_and_provider_graph()
    {
        using var builder =
            await DistributedApplicationTestingBuilder.CreateAsync<Projects.DigitalBrain_AppHost>();

        Assert.Single(builder.Resources.OfType<DigitalBrainResource>());
        Assert.Single(builder.Resources.OfType<AnthropicResource>());
        Assert.Single(builder.Resources.OfType<OpenAIResource>());
        Assert.Equal(3, builder.Resources.OfType<OpenAIModelResource>().Count());
        var kernel = Assert.Single(
            builder.Resources.OfType<ProjectResource>(),
            resource => resource.Name == "kernel");
        var restrictedClient = Assert.Single(
            builder.Resources.OfType<ContainerResource>(),
            resource => resource.Name == "restricted-client");
        Assert.Single(restrictedClient.Annotations.OfType<ExplicitStartupAnnotation>());

        var restrictedConfiguration =
            await ExecutionConfigurationAsync(restrictedClient);
        var environmentKeys = restrictedConfiguration.EnvironmentVariables
            .Select(pair => pair.Key)
            .ToArray();
        Assert.Contains(
            "ConnectionStrings__brain-clustering",
            environmentKeys);
        Assert.DoesNotContain(
            environmentKeys,
            key =>
                key.Contains("Reminder", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("GrainStorage", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("Journal", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("Outbox", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("Anthropic", StringComparison.OrdinalIgnoreCase));

        var kernelConfiguration = await ExecutionConfigurationAsync(kernel);
        var kernelEnvironmentKeys = kernelConfiguration.EnvironmentVariables
            .Select(pair => pair.Key)
            .ToArray();
        Assert.Contains(
            kernelEnvironmentKeys,
            key => key.StartsWith("Orleans__GrainStorage__", StringComparison.Ordinal));
        Assert.Contains(
            kernelEnvironmentKeys,
            key => key.StartsWith("Orleans__Streaming__", StringComparison.Ordinal));
        Assert.Contains("DigitalBrain__Storage__Journal", kernelEnvironmentKeys);
        Assert.Contains("DigitalBrain__AI__OpenAI__ApiKey", kernelEnvironmentKeys);
        Assert.Contains("DigitalBrain__AI__Anthropic__ApiKey", kernelEnvironmentKeys);
    }

    private static Task<IExecutionConfigurationResult> ExecutionConfigurationAsync(
        IResource resource) =>
        ExecutionConfigurationBuilder.Create(resource)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(
                new DistributedApplicationExecutionContext(
                    DistributedApplicationOperation.Publish),
                NullLogger.Instance,
                CancellationToken.None);
}
