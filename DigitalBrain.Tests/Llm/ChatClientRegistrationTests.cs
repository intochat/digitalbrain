using DigitalBrain.Core.Models;
using DigitalBrain.Kernel.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests.Llm;

public class ChatClientRegistrationTests
{
    [Fact]
    public void NoProviderConfigured_DoesNotRegisterChatClient()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddDigitalBrainChat(config);
        using var sp = services.BuildServiceProvider();
        Assert.Null(sp.GetService<IChatClient>());
    }

    [Fact]
    public void OllamaConfigured_RegistersChatClient()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:Llm:Provider"] = "ollama",
            ["DigitalBrain:Llm:OllamaEndpoint"] = "http://localhost:11434",
            ["DigitalBrain:Llm:Model"] = "qwen2.5-coder:1.5b",
        }).Build();
        var services = new ServiceCollection();
        services.AddDigitalBrainChat(config);
        using var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetService<IChatClient>());
    }

    [Fact]
    public void RegistryDefaultLlmConfigured_RegistersChatClient()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:ModelRegistry:DefaultLlm:Provider"] = DigitalBrainProviderIds.Ollama,
            ["DigitalBrain:ModelRegistry:DefaultLlm:Id"] = "qwen2.5-coder:1.5b",
            ["DigitalBrain:Llm:OllamaEndpoint"] = "http://localhost:11434",
        }).Build();
        var services = new ServiceCollection();
        services.AddDigitalBrainChat(config);
        using var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetService<IChatClient>());
    }

    [Fact]
    public void RegistryDefaultLlmWinsOverLegacyLlmKeys()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:ModelRegistry:DefaultLlm:Provider"] = DigitalBrainProviderIds.Ollama,
            ["DigitalBrain:ModelRegistry:DefaultLlm:Id"] = "registry-model",
            ["DigitalBrain:Llm:Provider"] = DigitalBrainProviderIds.AzureOpenAI,
            ["DigitalBrain:Llm:Model"] = "legacy-model",
        }).Build();

        var options = DigitalBrainLlmRuntimeOptions.FromConfiguration(config);

        Assert.Equal(DigitalBrainProviderIds.Ollama, options.Provider);
        Assert.Equal("registry-model", options.Model);
    }

    [Fact]
    public void OpenAiScopedModelCanComeFromModelRegistry()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:ModelRegistry:Registrations:0:Kind"] = DigitalBrainCapabilityKind.LargeLanguageModel.ToString(),
            ["DigitalBrain:ModelRegistry:Registrations:0:Provider"] = DigitalBrainProviderIds.OpenAI,
            ["DigitalBrain:ModelRegistry:Registrations:0:Id"] = "gpt-test",
            ["DigitalBrain:Llm:OpenAIModel"] = "legacy-openai",
        }).Build();

        var options = DigitalBrainLlmRuntimeOptions.FromConfiguration(config);

        Assert.Equal("gpt-test", options.OpenAIModel);
    }
}
