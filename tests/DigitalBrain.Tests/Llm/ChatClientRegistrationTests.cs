using System.Diagnostics;
using DigitalBrain.Core.Models;
using DigitalBrain.Kernel.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests.Llm;

[Collection("chat-telemetry-environment")]
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

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("not-a-boolean", false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public async Task ChatTelemetryHonorsStandardMessageCaptureEnvironmentVariable(string? configured, bool expected)
    {
        const string environmentVariable = "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT";
        const string prompt = "telemetry-secret-prompt";
        var previous = Environment.GetEnvironmentVariable(environmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(environmentVariable, configured);
            var stopped = new List<Activity>();
            using var listener = new ActivityListener
            {
                ShouldListenTo = static source => source.Name == "DigitalBrain.Neuron",
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = stopped.Add
            };
            ActivitySource.AddActivityListener(listener);
            using var client = DigitalBrainChatTelemetry.Wrap(new EchoChatClient());

            await client.GetResponseAsync(prompt);

            var telemetry = string.Join('\n', stopped.SelectMany(static activity =>
                activity.Tags.Select(static tag => $"{tag.Key}={tag.Value}")
                    .Concat(activity.Events.SelectMany(static activityEvent => activityEvent.Tags.Select(tag =>
                        $"{activityEvent.Name}:{tag.Key}={tag.Value}")))));
            Assert.Equal(expected, telemetry.Contains(prompt, StringComparison.Ordinal));
            Assert.Equal(expected, telemetry.Contains("telemetry-response", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, previous);
        }
    }

    private sealed class EchoChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "telemetry-response")));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}

[CollectionDefinition("chat-telemetry-environment", DisableParallelization = true)]
public sealed class ChatTelemetryEnvironmentCollection;
