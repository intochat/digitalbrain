using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Brain.Modules.AI.Contracts;
using Brain.Modules.AI.Aspire.Hosting;
using DigitalBrain.Aspire.Hosting;
using Xunit;

namespace Brain.Aspire.Hosting.Tests;

public sealed class AIHostingExtensionsTests
{
    [Fact]
    public void Gemma4_is_module_owned_and_projected_only_to_the_runtime()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");

        brain.AddModule<AiModule>(ai => ai.WithGemma4());

        var runtime = builder
            .AddExecutable("runtime", "dotnet", ".")
            .WithReference(brain);
        var client = builder
            .AddExecutable("client", "dotnet", ".")
            .WithReference(brain.AsClient());

        var ollama = Assert.Single(builder.Resources, resource => resource.Name == "ollama");
        Assert.Contains(
            ollama.Annotations.OfType<ContainerImageAnnotation>(),
            image => image.Tag == "latest");
        var model = Assert.Single(builder.Resources, resource => resource.Name == "gemma4-12b");
        Assert.Contains(
            runtime.Resource.Annotations.OfType<WaitAnnotation>(),
            annotation => annotation.Resource == model && annotation.WaitType == WaitType.WaitUntilHealthy);
        Assert.True(runtime.Resource.TryGetEnvironmentVariables(out var runtimeEnvironment));
        Assert.True(client.Resource.TryGetEnvironmentVariables(out var clientEnvironment));
        Assert.True(runtimeEnvironment.Count() >= clientEnvironment.Count() + 2);
    }
}
