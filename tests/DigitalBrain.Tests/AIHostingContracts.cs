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

    [Fact(DisplayName = "AppHost rejects duplicate AI modules and duplicate typed LLMs")]
    public void AppHostRejectsDuplicateSelections()
    {
        var moduleBuilder = DistributedApplication.CreateBuilder();
        var moduleBrain = moduleBuilder.AddBrain("module-brain");

        moduleBrain.AddModule<AIModule>(ai => ai.WithLlm<Llama32>());

        Assert.Throws<InvalidOperationException>(
            () => moduleBrain.AddModule<AIModule>(ai => ai.WithLlm<Llama32>()));

        var modelBuilder = DistributedApplication.CreateBuilder();
        var modelBrain = modelBuilder.AddBrain("model-brain");

        Assert.Throws<InvalidOperationException>(
            () => modelBrain.AddModule<AIModule>(ai => ai
                .WithLlm<Llama32>()
                .WithLlm<Llama32>()));
    }

    [Fact(DisplayName = "OpenAI configuration is a documented secret parameter")]
    public void OpenAIUsesDocumentedSecretParameter()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder
            .AddBrain("brain")
            .WithDevelopmentStores();

        brain.AddModule<AIModule>(ai => ai.WithLlm<Gpt56>());

        var apiKey = Assert.IsType<ParameterResource>(
            Assert.Single(builder.Resources, resource => resource.Name == "openai-api-key"));

        Assert.True(apiKey.Secret);
        Assert.True(apiKey.EnableDescriptionMarkdown);
        Assert.Contains(
            "[OpenAI Platform](https://platform.openai.com/api-keys)",
            apiKey.Description,
            StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Ollama configuration creates no secret parameter")]
    public void OllamaCreatesNoSecretParameter()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddBrain("brain");

        brain.AddModule<AIModule>(ai => ai.WithLlm<Llama32>());

        Assert.DoesNotContain(builder.Resources, resource => resource is ParameterResource);
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

    [Fact(DisplayName = "OpenAI publish projection references the secret instead of embedding it")]
    public async Task OpenAIProjectionContainsOnlyAParameterExpression()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder
            .AddBrain("brain")
            .WithDevelopmentStores();

        brain.AddModule<AIModule>(ai => ai.WithLlm<Gpt56>());

        var silo = builder.AddResource(new ProjectionProbe("silo")).WithReference(brain);
        var environment = await ProjectAsync(silo.Resource);
        var apiKey = Assert.IsType<ParameterResource>(
            Assert.Single(builder.Resources, resource => resource.Name == "openai-api-key"));

        Assert.Same(apiKey, environment["DigitalBrain__AI__OpenAI__ApiKey"]);
    }

    private static async Task<Dictionary<string, object>> ProjectAsync(IResourceWithEnvironment resource)
    {
        var context = new EnvironmentCallbackContext(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish));

        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return context.EnvironmentVariables.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.Ordinal);
    }

    private sealed class ProjectionProbe(string name) : Resource(name), IResourceWithEnvironment, IResourceWithEndpoints;
}
