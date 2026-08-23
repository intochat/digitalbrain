using System.Reflection;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ProductionEmbeddingRegistrationTests
{
    [Fact]
    public void ExplicitOllamaEmbeddingModelResolvesAsAnEmbeddingGenerator()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["DigitalBrain:AI:Ollama:Endpoint"] = "http://127.0.0.1:11434",
                    ["DigitalBrain:AI:Ollama:IEmbeddingGemma:Model"] = "embeddinggemma",
                })
            .Build());

        var clients = typeof(AIModule).Assembly.GetType("DigitalBrain.AI.AIClients", throwOnError: true)!;
        clients.GetMethod("Add", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [services]);

        using var provider = services.BuildServiceProvider();
        var embedding = provider.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>(
            typeof(IEmbeddingGemma));

        Assert.IsAssignableFrom<IEmbeddingGenerator<string, Embedding<float>>>(embedding);
    }
}
