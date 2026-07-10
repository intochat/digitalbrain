using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Kernel.Hosting;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.Kernel.V2;
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
    public async Task Actual_v2_kernel_graph_has_no_legacy_gateway_bus_or_stream_provider()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ApplicationName = typeof(DigitalBrainOrleansExtensions).Assembly.GetName().Name
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:Runtime"] = "V2",
            ["DigitalBrain:TestMode"] = "true",
            ["DigitalBrain:Llm:Provider"] = "ollama",
            ["DigitalBrain:Llm:OllamaEndpoint"] = "http://localhost:11434",
            ["DigitalBrain:Llm:Model"] = "test-model"
        });
        builder.AddServiceDefaults();
        builder.UseDigitalBrainOrleans();
        builder.AddDigitalBrainClients();

        var descriptors = builder.Services.ToArray();
        Assert.DoesNotContain(descriptors, descriptor => descriptor.ServiceType == typeof(GatewayService));
        Assert.DoesNotContain(descriptors, descriptor => descriptor.ServiceType == typeof(UiGatewayService));
        Assert.DoesNotContain(descriptors, descriptor => descriptor.ServiceType == typeof(HomeFeedBus));
        Assert.DoesNotContain(descriptors, descriptor => descriptor.ServiceType == typeof(SignalEgressBus));
        Assert.DoesNotContain(descriptors, descriptor =>
            string.Equals(descriptor.ServiceType.FullName, "DigitalBrain.Kernel.Ui.SignalEgressStreamSubscriber", StringComparison.Ordinal) ||
            string.Equals(descriptor.ImplementationType?.FullName, "DigitalBrain.Kernel.Ui.SignalEgressStreamSubscriber", StringComparison.Ordinal));

        var graph = string.Join('\n', descriptors.Select(static descriptor =>
            $"{descriptor.ServiceType.FullName}|{descriptor.ServiceKey}|{descriptor.ImplementationType?.FullName}"));
        Assert.DoesNotContain("HomeFeed", graph, StringComparison.Ordinal);
        Assert.DoesNotContain(SynapseStream.ProviderName, graph, StringComparison.Ordinal);
        Assert.DoesNotContain("PubSubStore", graph, StringComparison.Ordinal);

        await using var app = builder.Build();
        Assert.NotNull(app.Services.GetService<IChatClient>());
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
