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
            ["DigitalBrain:Llm:Model"] = "llama3.1:8b",
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
            ["DigitalBrain:ModelRegistry:DefaultLlm:Id"] = "llama3.1:8b",
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
    public void AzureOpenAIConfiguredWithKey_RegistersChatClient()
    {
        // Guards the pre-existing key path (Task 19 must leave this exact behavior untouched): construction
        // with an AzureKeyCredential doesn't touch the network, so this exercises the real branch rather than
        // a stub.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:Llm:Provider"] = DigitalBrainProviderIds.AzureOpenAI,
            ["DigitalBrain:Llm:AzureOpenAIEndpoint"] = "https://digitalbrainopenaiprod.openai.azure.com/",
            ["DigitalBrain:Llm:AzureOpenAIKey"] = "test-key",
            ["DigitalBrain:Llm:Model"] = "chat",
        }).Build();
        var services = new ServiceCollection();
        services.AddDigitalBrainChat(config);
        using var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetService<IChatClient>());
    }

    [Fact]
    public void AzureOpenAIConfiguredWithoutKey_FallsBackToManagedIdentity_RegistersChatClient()
    {
        // Task 19 step 3: when no key is configured (the shape a real ACA deploy will have once a follow-up
        // deploy removes the key env var per the plan's two-deploy sequencing), DigitalBrainChat must build a
        // DefaultAzureCredential-backed client instead of throwing. DefaultAzureCredential's construction
        // doesn't probe any credential source or touch the network, so this is safe to exercise directly
        // without live Azure infra.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:Llm:Provider"] = DigitalBrainProviderIds.AzureOpenAI,
            ["DigitalBrain:Llm:AzureOpenAIEndpoint"] = "https://digitalbrainopenaiprod.openai.azure.com/",
            ["DigitalBrain:Llm:Model"] = "chat",
        }).Build();
        var services = new ServiceCollection();
        services.AddDigitalBrainChat(config);
        using var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetService<IChatClient>());
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

    [Fact]
    public void DirectOpenAIConfiguredWithKey_RegistersChatClient()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:Llm:Provider"] = DigitalBrainProviderIds.OpenAI,
            ["DigitalBrain:Llm:OpenAIApiKey"] = "test-openai-key",
            ["DigitalBrain:Llm:Model"] = "gpt-test",
        }).Build();

        var services = new ServiceCollection();
        services.AddDigitalBrainChat(config);
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<IChatClient>());
    }

    [Fact]
    public void GitHubModelsConfiguredWithToken_RegistersChatClient()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:Llm:Provider"] = DigitalBrainProviderIds.GitHubModels,
            ["DigitalBrain:Llm:GitHubModelsToken"] = "test-github-token",
            ["DigitalBrain:Llm:Model"] = "openai/gpt-4.1-mini",
        }).Build();

        var services = new ServiceCollection();
        services.AddDigitalBrainChat(config);
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<IChatClient>());
    }
}
