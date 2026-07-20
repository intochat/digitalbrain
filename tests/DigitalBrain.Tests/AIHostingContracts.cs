using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.AI.Ollama;
using DigitalBrain.AI.OpenAI;
using DigitalBrain.Aspire.Hosting;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class AIHostingContracts
{
    [Fact(DisplayName = "AppHost configures typed LLMs through its AI module")]
    public void AppHostSurfaceCreatesOneResourcePerProvider()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder
            .AddBrain("brain")
            .WithDevelopmentStores();

        brain.AddModule<AIModule>(ai => ai
            .WithLlm<Llama32>()
            .WithLlm<Gpt56>());

        Assert.Single(builder.Resources, resource => resource.Name == "brain-ai-ollama");
        Assert.Single(builder.Resources, resource => resource.Name == "brain-ai-openai");
        Assert.Single(builder.Resources, resource => resource.Name == "openai-api-key");
    }

    [Fact(DisplayName = "AI configuration is projected only to the silo reference")]
    public async Task AIConfigurationIsProjectedOnlyToTheSilo()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder
            .AddBrain("brain")
            .WithDevelopmentStores();

        brain.AddModule<AIModule>(ai => ai.WithLlm<Llama32>());

        var silo = builder.AddResource(new ProjectionProbe("silo")).WithReference(brain);
        var client = builder.AddResource(new ProjectionProbe("client")).WithReference(brain.AsClient());

        var siloEnvironment = await ProjectAsync(silo.Resource);
        var clientEnvironment = await ProjectAsync(client.Resource);

        Assert.Contains("DigitalBrain__Modules__0", siloEnvironment.Keys);
        Assert.Contains("DigitalBrain__AI__Ollama__Endpoint", siloEnvironment.Keys);
        Assert.Contains("DigitalBrain__AI__Ollama__Llama32__Model", siloEnvironment.Keys);
        Assert.DoesNotContain(clientEnvironment.Keys, key => key.StartsWith("DigitalBrain__", StringComparison.Ordinal));
    }

    private static async Task<Dictionary<string, string>> ProjectAsync(IResourceWithEnvironment resource)
    {
        var context = new EnvironmentCallbackContext(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish));

        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return context.EnvironmentVariables.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.ToString() ?? string.Empty,
            StringComparer.Ordinal);
    }

    private sealed class ProjectionProbe(string name) : Resource(name), IResourceWithEnvironment, IResourceWithEndpoints;
}
