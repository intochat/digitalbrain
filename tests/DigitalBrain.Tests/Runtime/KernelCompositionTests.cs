using DigitalBrain.Kernel;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Core.Config;
using DigitalBrain.Google;
using DigitalBrain.Kernel.Hosting;
using DigitalBrain.Kernel.Abstractions;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.RuntimeHost;
using DigitalBrain.Salesforce;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Tests.Runtime;

public sealed class KernelCompositionTests
{
    [Fact]
    public async Task Conversation_model_grain_sends_the_prompt_through_the_configured_chat_client()
    {
        const string prompt = "What can you help me with in this workspace?";
        var chat = new RecordingPromptDependentChatClient();
        var grain = new ConversationModelGrain(chat);

        var response = await grain.CompleteAsync(new ConversationModelCompletionRequest(prompt, []));

        Assert.Contains(prompt, chat.LastRequest, StringComparison.Ordinal);
        Assert.Contains(prompt, response.Text, StringComparison.Ordinal);
        Assert.Equal("configured", response.Model);
    }

    [Fact]
    public async Task Conversation_model_receives_authorized_sender_metadata_without_a_blanket_identifier_refusal()
    {
        var chat = new RecordingPromptDependentChatClient();
        var grain = new ConversationModelGrain(chat);
        const string grounded = "{\"latestIncomingMessage\":{\"status\":\"senderAvailable\",\"sender\":\"Ada Lovelace <ada@example.com>\",\"senderAddress\":\"ada@example.com\"}}";

        await grain.CompleteAsync(new ConversationModelCompletionRequest(
            "Who sent my last email to me? Give me the sender’s email address.",
            [],
            [new ConversationModelToolOutcome("Success", grounded, null)]));

        Assert.Contains("ada@example.com", chat.LastRequest, StringComparison.Ordinal);
        Assert.Contains("internal identifiers", chat.LastRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("Never expose identifiers", chat.LastRequest, StringComparison.Ordinal);
        Assert.Equal(2, Count(chat.LastRequest, "ada@example.com"));
        Assert.DoesNotContain("\\\"latestIncomingMessage\\\"", chat.LastRequest, StringComparison.Ordinal);
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
        builder.AddDigitalBrainRuntimeHost();

        var descriptors = builder.Services.ToArray();
        Assert.Contains(descriptors, descriptor => descriptor.ServiceType == typeof(IPackConfigStore));
        Assert.Contains(descriptors, descriptor => descriptor.ServiceType == typeof(IGmailApiClientFactory));
        Assert.Contains(descriptors, descriptor => descriptor.ServiceType == typeof(ISalesforceApiClientFactory));
        Assert.Contains(descriptors, descriptor =>
            descriptor.ServiceType == typeof(IInoToolGateway) &&
            descriptor.ImplementationType == typeof(ClosedInoToolGateway));
        Assert.DoesNotContain(descriptors, descriptor =>
            descriptor.ServiceType == typeof(IInoToolGateway) &&
            descriptor.ImplementationType == typeof(PlanInoToolGateway));
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
        app.MapDigitalBrainRuntimeHost();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(static source => source.Endpoints).ToArray();
        var endpointGraph = string.Join('\n', endpoints.Select(static endpoint =>
            endpoint is RouteEndpoint route ? $"{endpoint.DisplayName}|{route.RoutePattern.RawText}" : endpoint.DisplayName));
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

    private static int Count(string value, string expected)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(expected, index, StringComparison.Ordinal)) >= 0; index += expected.Length)
            count++;
        return count;
    }
}
