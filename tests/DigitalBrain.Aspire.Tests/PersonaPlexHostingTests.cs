using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Aspire.Tests;

#pragma warning disable ASPIREPROBES001 // Probe annotation is the Aspire resource-model assertion surface.

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
        Assert.Null(parameter.Default);
        Assert.True(parameter.EnableDescriptionMarkdown);
        Assert.Contains("https://huggingface.co/nvidia/personaplex-7b-v1", parameter.Description, StringComparison.Ordinal);
        Assert.Contains("https://huggingface.co/settings/tokens", parameter.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostGeneratesAndPersistsSeparatePersonaPlexAdapterToken()
    {
        var parameter = Assert.Single(
            fixture.Model.Resources.OfType<ParameterResource>(),
            static resource => resource.Name == "personaplex-adapter-token");

        Assert.True(parameter.Secret);
        Assert.Equal("UserSecretsParameterDefault", parameter.Default?.GetType().Name);
    }

    [Fact]
    public void AppHostDefinesPrivatePersonaPlexDockerfileRuntimeWithPersistentCacheAndReadiness()
    {
        var runtime = Assert.IsType<ContainerResource>(fixture.Model.Resource("personaplex-runtime"));
        var dockerfile = Assert.Single(runtime.Annotations.OfType<DockerfileBuildAnnotation>());
        var cache = Assert.Single(runtime.Annotations.OfType<ContainerMountAnnotation>());

        Assert.EndsWith("src\\Runtime\\PersonaPlex", dockerfile.ContextPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            Path.Combine("Runtime", "PersonaPlex", "Dockerfile"),
            dockerfile.DockerfilePath,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/var/cache/huggingface", cache.Target);
        Assert.Equal("personaplex-huggingface-cache", cache.Source);
        Assert.Contains(runtime.Annotations, static annotation => annotation is HealthCheckAnnotation);
        Assert.Contains(runtime.Annotations, static annotation => annotation is EndpointProbeAnnotation probe
            && probe.Path == "/readyz" && probe.EndpointReference.EndpointName == "http");

        var endpoint = Assert.Single(runtime.Annotations.OfType<EndpointAnnotation>());
        Assert.Equal("http", endpoint.Name);
        Assert.Equal(8080, endpoint.TargetPort);
        // Aspire records the container's internal port here; IsExternal=false and
        // IsProxied=false are the controls that prevent a host-published endpoint.
        Assert.Equal(8080, endpoint.Port);
        Assert.False(endpoint.IsExternal);
        Assert.False(endpoint.IsProxied);
    }

    [Fact]
    public async Task KernelRenderedEnvironmentContainsPersonaPlexEnabledSetting()
    {
        var environment = await fixture.Model.RenderedEnvironmentAsync(ProductSurfaceResourceNames.Kernel);

        Assert.True(environment.ContainsKey("DigitalBrain__AI__PersonaPlex__Enabled"));
    }

    [Fact]
    public async Task PersonaPlexSecretsAndRuntimeEndpointAreInjectedOnlyIntoAuthorizedResources()
    {
        var runtimeEnvironment = await fixture.Model.RenderedEnvironmentAsync("personaplex-runtime");
        var kernelEnvironment = await fixture.Model.RenderedEnvironmentAsync(ProductSurfaceResourceNames.Kernel);

        Assert.Equal("{personaplex-hugging-face-token.value}", runtimeEnvironment["HF_TOKEN"]);
        Assert.Equal("{personaplex-adapter-token.value}", runtimeEnvironment["PERSONAPLEX_ADAPTER_TOKEN"]);
        Assert.Equal("{personaplex-adapter-token.value}", kernelEnvironment["DigitalBrain__AI__PersonaPlex__AdapterToken"]);
        Assert.Contains("personaplex-runtime", kernelEnvironment["DigitalBrain__AI__PersonaPlex__RuntimeEndpoint"], StringComparison.Ordinal);
        Assert.Equal("True", kernelEnvironment["DigitalBrain__AI__PersonaPlex__UseRemoteRuntime"]);
        Assert.DoesNotContain("HF_TOKEN", kernelEnvironment.Keys);
        Assert.DoesNotContain("personaplex-hugging-face-token", kernelEnvironment.Values, StringComparer.Ordinal);
    }

    [Fact]
    public void KernelWaitsForPersonaPlexRuntimeReadiness()
    {
        var kernel = fixture.Model.Resource(ProductSurfaceResourceNames.Kernel);

        Assert.Contains(kernel.Annotations, static annotation => annotation is WaitAnnotation wait
            && wait.Resource.Name == "personaplex-runtime"
            && wait.WaitType == WaitType.WaitUntilHealthy);
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

#pragma warning restore ASPIREPROBES001
