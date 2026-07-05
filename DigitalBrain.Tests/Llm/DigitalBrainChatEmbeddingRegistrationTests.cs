using DigitalBrain.Kernel.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Llm;

public class DigitalBrainChatEmbeddingRegistrationTests
{
    [Fact]
    public void AddDigitalBrainChat_RegistersOllamaEmbeddingGenerator_WhenConfigured()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:ModelRegistry:DefaultEmbedding:Provider"] = "ollama",
                ["DigitalBrain:ModelRegistry:DefaultEmbedding:Id"] = "nomic-embed-text",
                ["DigitalBrain:Embedding:OllamaEndpoint"] = "http://localhost:11434"
            })
            .Build();

        services.AddDigitalBrainChat(config);
        var provider = services.BuildServiceProvider();

        var embedder = provider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

        Assert.IsNotType<NoOpEmbeddingGenerator>(embedder);
    }

    [Fact]
    public void AddDigitalBrainChat_FailsSoftToNoOp_WhenEmbeddingNotConfigured()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        services.AddDigitalBrainChat(config);
        var provider = services.BuildServiceProvider();

        var embedder = provider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

        Assert.IsType<NoOpEmbeddingGenerator>(embedder);
    }
}
