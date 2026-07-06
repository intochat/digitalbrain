using DigitalBrain.Kernel.Llm;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Tests.Llm;

public class DigitalBrainEmbeddingRuntimeOptionsTests
{
    [Fact]
    public void FromConfiguration_ReadsRegistryEmittedKeys()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:ModelRegistry:DefaultEmbedding:Provider"] = "ollama",
                ["DigitalBrain:ModelRegistry:DefaultEmbedding:Id"] = "nomic-embed-text",
                ["DigitalBrain:Embedding:OllamaEndpoint"] = "http://localhost:11434"
            })
            .Build();

        var options = DigitalBrainEmbeddingRuntimeOptions.FromConfiguration(config);

        Assert.Equal("ollama", options.Provider);
        Assert.Equal("nomic-embed-text", options.Model);
        Assert.Equal("http://localhost:11434", options.OllamaEndpoint);
    }

    [Fact]
    public void FromConfiguration_ReturnsNullProvider_WhenNothingConfigured()
    {
        var config = new ConfigurationBuilder().Build();

        var options = DigitalBrainEmbeddingRuntimeOptions.FromConfiguration(config);

        Assert.Null(options.Provider);
    }
}
