using System.Reflection;
using DigitalBrain.AI;
using DigitalBrain.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ProductionLlmRegistrationTests
{
    private static readonly Dictionary<string, string?> AllProvidersConfigured = new()
    {
        ["DigitalBrain:AI:OpenAI:ApiKey"] = "test-openai-key",
        ["DigitalBrain:AI:Anthropic:ApiKey"] = "test-anthropic-key",
        ["DigitalBrain:AI:Google:ApiKey"] = "test-google-key",
        ["DigitalBrain:AI:XAI:ApiKey"] = "test-xai-key",
        ["DigitalBrain:AI:Ollama:Endpoint"] = "http://127.0.0.1:11434",
    };

    [Fact]
    public void EveryLlmModelResolvesAsAChatClientWhenItsProviderIsConfigured()
    {
        using var provider = BuildProvider(AllProvidersConfigured);

        Assert.All(LLMModel.All, model =>
            Assert.NotNull(provider.GetRequiredKeyedService<IChatClient>(model.Marker)));
    }

    [Fact]
    public void EveryEmbeddingModelResolvesAsAnEmbeddingGeneratorWhenItsProviderIsConfigured()
    {
        using var provider = BuildProvider(AllProvidersConfigured);

        Assert.All(EmbeddingModel.All, model =>
            Assert.NotNull(provider.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>(
                model.Marker)));
    }

    [Fact]
    public void MissingProviderApiKeyFailsWithConfigurationGuidance()
    {
        using var provider = BuildProvider([]);

        var failure = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredKeyedService<IChatClient>(typeof(IGpt54)));

        Assert.Contains("DigitalBrain:AI:OpenAI:ApiKey", failure.Message, StringComparison.Ordinal);
        Assert.Contains("WithLlm<IGpt54>", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultChatClientFallsBackToTheFirstConfiguredProvider()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["DigitalBrain:AI:Ollama:Endpoint"] = "http://127.0.0.1:11434",
        });

        Assert.NotNull(provider.GetRequiredService<IChatClient>());
    }

    [Fact]
    public void UnknownDefaultModelNameFailsListingKnownModels()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["DigitalBrain:AI:Default:Model"] = "IDoesNotExist",
        });

        var failure = Assert.Throws<InvalidOperationException>(
            provider.GetRequiredService<IChatClient>);

        Assert.Contains("IDoesNotExist", failure.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IGpt54), failure.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(configuration)
            .Build());

        var clients = typeof(AIModule).Assembly.GetType("DigitalBrain.AI.AIClients", throwOnError: true)!;
        clients.GetMethod("Add", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [services]);

        return services.BuildServiceProvider();
    }
}
