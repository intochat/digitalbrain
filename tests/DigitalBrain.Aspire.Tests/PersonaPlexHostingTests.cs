using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Aspire.Tests;

[Collection(ModelCollection.Name)]
public sealed class PersonaPlexHostingTests(ModelFixture fixture)
{
    [Fact]
    public void AppHostExposesSecretPersonaPlexHuggingFaceTokenWithModelAccessGuidance()
    {
        var parameter = Assert.Single(
            fixture.Model.Resources.OfType<ParameterResource>(),
            static resource => resource.Name == "personaplex-hugging-face-token");

        Assert.True(parameter.Secret);
        Assert.True(parameter.EnableDescriptionMarkdown);
        Assert.Contains("https://huggingface.co/nvidia/personaplex-7b-v1", parameter.Description, StringComparison.Ordinal);
        Assert.Contains("https://huggingface.co/settings/tokens", parameter.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KernelRenderedEnvironmentContainsPersonaPlexEnabledSetting()
    {
        var environment = await fixture.Model.RenderedEnvironmentAsync(ProductSurfaceResourceNames.Kernel);

        Assert.True(environment.ContainsKey("DigitalBrain__AI__PersonaPlex__Enabled"));
    }

    [Fact]
    public async Task KernelRenderedEnvironmentProjectsConfiguredPersonaPlexValues()
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var brain = builder.AddDigitalBrain("brain");
        brain.AddModule<AIModule>(ai => ai.WithPersonaPlex(options =>
        {
            options.Enabled = true;
            options.ModelDirectory = "C:\\models\\personaplex";
            options.CudaDeviceId = 3;
            options.MaxSessions = 2;
        }));
        var kernel = builder
            .AddExecutable("kernel", "true", ".")
            .WithHttpEndpoint(port: 0, name: "http")
            .WithReference(brain);

        var configuration = await ExecutionConfigurationBuilder.Create(kernel.Resource)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(
                new(DistributedApplicationOperation.Publish),
                NullLogger.Instance,
                TestContext.Current.CancellationToken);
        var environment = configuration.EnvironmentVariables.ToDictionary();

        Assert.Equal("True", environment["DigitalBrain__AI__PersonaPlex__Enabled"]);
        Assert.Equal("C:\\models\\personaplex", environment["DigitalBrain__AI__PersonaPlex__ModelDirectory"]);
        Assert.Equal("3", environment["DigitalBrain__AI__PersonaPlex__CudaDeviceId"]);
        Assert.Equal("2", environment["DigitalBrain__AI__PersonaPlex__MaxSessions"]);
    }
}
