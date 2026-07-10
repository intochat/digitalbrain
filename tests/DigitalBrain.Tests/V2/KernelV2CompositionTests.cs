using DigitalBrain.Kernel;
using DigitalBrain.Core.Config;
using DigitalBrain.Google;
using DigitalBrain.Kernel.Hosting;
using DigitalBrain.Kernel.Abstractions;
using DigitalBrain.Kernel.V2;
using DigitalBrain.Salesforce;
using DigitalBrain.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Tests.V2;

public sealed class KernelV2CompositionTests
{
    [Fact]
    public async Task Conversation_model_grain_sends_the_prompt_through_the_configured_chat_client()
    {
        const string prompt = "What can you help me with in this workspace?";
        var chat = new RecordingPromptDependentChatClient();
        var grain = new V2ConversationModelGrain(chat);

        var response = await grain.CompleteAsync(new V2ConversationModelCompletionRequest(prompt, []));

        Assert.Contains(prompt, chat.LastRequest, StringComparison.Ordinal);
        Assert.Contains(prompt, response.Text, StringComparison.Ordinal);
        Assert.Equal("configured", response.Model);
    }

    [Fact]
    public async Task Production_kernel_graph_has_one_runtime_and_shared_connector_composition()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ApplicationName = typeof(DigitalBrainOrleansExtensions).Assembly.GetName().Name
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:TestMode"] = "true",
            ["DigitalBrain:Llm:Provider"] = "ollama",
            ["DigitalBrain:Llm:OllamaEndpoint"] = "http://localhost:11434",
            ["DigitalBrain:Llm:Model"] = "test-model"
        });
        builder.AddServiceDefaults();
        builder.UseDigitalBrainOrleans();
        builder.AddDigitalBrainClients();

        var descriptors = builder.Services.ToArray();
        Assert.DoesNotContain(descriptors, descriptor =>
            string.Equals(descriptor.ServiceType.FullName, "DigitalBrain.Kernel.Ui.SignalEgressStreamSubscriber", StringComparison.Ordinal) ||
            string.Equals(descriptor.ImplementationType?.FullName, "DigitalBrain.Kernel.Ui.SignalEgressStreamSubscriber", StringComparison.Ordinal));
        Assert.Contains(descriptors, descriptor => descriptor.ServiceType == typeof(IPackConfigStore));
        Assert.Contains(descriptors, descriptor => descriptor.ServiceType == typeof(IGmailApiClientFactory));
        Assert.Contains(descriptors, descriptor => descriptor.ServiceType == typeof(ISalesforceApiClientFactory));
        Assert.Contains(descriptors, descriptor => descriptor.ServiceType == typeof(IConnector) && Equals(descriptor.ServiceKey, "google"));
        Assert.Contains(descriptors, descriptor => descriptor.ServiceType == typeof(IConnector) && Equals(descriptor.ServiceKey, "salesforce"));

        var graph = string.Join('\n', descriptors.Select(static descriptor =>
            $"{descriptor.ServiceType.FullName}|{descriptor.ServiceKey}|{descriptor.ImplementationType?.FullName}"));
        Assert.DoesNotContain("HomeFeed", graph, StringComparison.Ordinal);
        Assert.DoesNotContain(SynapseStream.ProviderName, graph, StringComparison.Ordinal);
        Assert.DoesNotContain("PubSubStore", graph, StringComparison.Ordinal);

        await using var app = builder.Build();
        Assert.NotNull(app.Services.GetService<IChatClient>());
        Assert.NotNull(app.Services.GetRequiredService<IPackConfigStore>());
        Assert.NotNull(app.Services.GetRequiredService<IGmailApiClientFactory>());
        Assert.NotNull(app.Services.GetRequiredService<ISalesforceApiClientFactory>());
        Assert.NotNull(app.Services.GetRequiredKeyedService<IConnector>("google"));
        Assert.NotNull(app.Services.GetRequiredKeyedService<IConnector>("salesforce"));
        app.MapDigitalBrainSetup();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(static source => source.Endpoints).ToArray();
        var endpointGraph = string.Join('\n', endpoints.Select(static endpoint =>
            endpoint is RouteEndpoint route ? $"{endpoint.DisplayName}|{route.RoutePattern.RawText}" : endpoint.DisplayName));
        Assert.DoesNotContain("digitalbrain.DigitalBrainGateway", endpointGraph, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("digitalbrain.ui.UiGateway", endpointGraph, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WatchHomeFeed", endpointGraph, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WatchSynapses", endpointGraph, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingPromptDependentChatClient : IChatClient
    {
        public string LastRequest { get; private set; } = string.Empty;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = string.Concat(messages.Select(static message => message.Text));
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, $"Model answer for: {LastRequest}")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Streaming is not used by the conversation model port.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
