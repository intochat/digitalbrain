using DigitalBrain.Core.Models;
using DigitalBrain.Kernel.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public class DigitalBrainChatClientRegistrationTests
{
    [Fact]
    public void RegistersOneKeyedChatClientPerLlmRegistration()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:ModelRegistry:Registrations:0:Kind"] = "LargeLanguageModel",
            ["DigitalBrain:ModelRegistry:Registrations:0:Provider"] = "ollama",
            ["DigitalBrain:ModelRegistry:Registrations:0:Id"] = "qwen2.5-coder:1.5b",
            ["DigitalBrain:ModelRegistry:Registrations:0:ServiceKey"] = "ollama-qwen2-5-coder-1-5b",
            ["DigitalBrain:ModelRegistry:Registrations:0:Role"] = "Fast",
            ["DigitalBrain:ModelRegistry:Registrations:1:Kind"] = "LargeLanguageModel",
            ["DigitalBrain:ModelRegistry:Registrations:1:Provider"] = "ollama",
            ["DigitalBrain:ModelRegistry:Registrations:1:Id"] = "llama3.1:8b",
            ["DigitalBrain:ModelRegistry:Registrations:1:ServiceKey"] = "ollama-llama3-1-8b",
            ["DigitalBrain:ModelRegistry:Registrations:1:Role"] = "Reasoning",
            ["DigitalBrain:ModelRegistry:Registrations:1:SupportsTools"] = "true",
            ["DigitalBrain:Llm:OllamaEndpoint"] = "http://localhost:11434",
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddDigitalBrainChatClients(config);
        var provider = services.BuildServiceProvider();

        var fast = provider.GetKeyedService<IChatClient>("ollama-qwen2-5-coder-1-5b");
        var reasoning = provider.GetKeyedService<IChatClient>("ollama-llama3-1-8b");

        Assert.NotNull(fast);
        Assert.NotNull(reasoning);
        Assert.NotSame(fast, reasoning);
    }

    [Fact]
    public void RegistersAnthropicChatClientWhenApiKeyIsConfigured()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:ModelRegistry:Registrations:0:Kind"] = "LargeLanguageModel",
            ["DigitalBrain:ModelRegistry:Registrations:0:Provider"] = "anthropic",
            ["DigitalBrain:ModelRegistry:Registrations:0:Id"] = "claude-haiku-4-5",
            ["DigitalBrain:ModelRegistry:Registrations:0:ServiceKey"] = "anthropic-claude-haiku-4-5",
            ["DigitalBrain:ModelRegistry:Registrations:0:Role"] = "Fast",
            ["DigitalBrain:ModelRegistry:Registrations:0:SupportsTools"] = "true",
            ["DigitalBrain:Llm:AnthropicApiKey"] = "test-key",
        }).Build();

        var services = new ServiceCollection();
        services.AddDigitalBrainChatClients(config);
        var provider = services.BuildServiceProvider();

        var client = provider.GetKeyedService<IChatClient>("anthropic-claude-haiku-4-5");

        Assert.NotNull(client);
    }

    [Fact]
    public void ThrowsAClearErrorWhenAnthropicModelIsRegisteredWithoutAnApiKey()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:ModelRegistry:Registrations:0:Kind"] = "LargeLanguageModel",
            ["DigitalBrain:ModelRegistry:Registrations:0:Provider"] = "anthropic",
            ["DigitalBrain:ModelRegistry:Registrations:0:Id"] = "claude-haiku-4-5",
            ["DigitalBrain:ModelRegistry:Registrations:0:ServiceKey"] = "anthropic-claude-haiku-4-5",
        }).Build();

        var services = new ServiceCollection();
        services.AddDigitalBrainChatClients(config);
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredKeyedService<IChatClient>("anthropic-claude-haiku-4-5"));
        Assert.Contains("AnthropicApiKey", ex.Message);
    }

    [Fact]
    public void RegistersXaiChatClientWhenApiKeyIsConfigured()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:ModelRegistry:Registrations:0:Kind"] = "LargeLanguageModel",
            ["DigitalBrain:ModelRegistry:Registrations:0:Provider"] = "xai",
            ["DigitalBrain:ModelRegistry:Registrations:0:Id"] = "grok-4-1-fast",
            ["DigitalBrain:ModelRegistry:Registrations:0:ServiceKey"] = "xai-grok-4-1-fast",
            ["DigitalBrain:Llm:XaiApiKey"] = "test-key",
        }).Build();

        var services = new ServiceCollection();
        services.AddDigitalBrainChatClients(config);
        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetKeyedService<IChatClient>("xai-grok-4-1-fast"));
    }

    [Fact]
    public void ThrowsAClearErrorWhenXaiModelIsRegisteredWithoutAnApiKey()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:ModelRegistry:Registrations:0:Kind"] = "LargeLanguageModel",
            ["DigitalBrain:ModelRegistry:Registrations:0:Provider"] = "xai",
            ["DigitalBrain:ModelRegistry:Registrations:0:Id"] = "grok-4-1-fast",
            ["DigitalBrain:ModelRegistry:Registrations:0:ServiceKey"] = "xai-grok-4-1-fast",
        }).Build();

        var services = new ServiceCollection();
        services.AddDigitalBrainChatClients(config);
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredKeyedService<IChatClient>("xai-grok-4-1-fast"));
        Assert.Contains("XaiApiKey", ex.Message);
    }
}
