using System.Text.Json;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Integrations.Salesforce.Contracts;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.AI;

namespace DigitalBrain.OrleansTests.Capabilities;

public sealed class CapabilityParameterModelTests
{
    [Fact]
    public async Task ExtractAsync_rejects_a_model_selected_capability_change()
    {
        var chat = RecordingChatClientReturning(toolId: SalesforceCapabilityIds.RecordRead, argumentsJson: "{\"query\":\"Acme\"}");
        var model = new CapabilityParameterModel(chat);

        await Assert.ThrowsAsync<InvalidOperationException>(() => model.ExtractAsync(new CapabilityParameterRequest(
            Descriptor(GoogleCapabilityIds.GmailMessageRead, "Read Gmail messages"),
            "list recent mail")));
    }

    [Fact]
    public void Request_rejects_a_missing_selected_descriptor()
    {
        Assert.Throws<ArgumentNullException>(() => new CapabilityParameterRequest(null!, "list recent mail"));
    }

    [Fact]
    public async Task ExtractAsync_returns_the_server_selected_capability_with_the_extracted_arguments()
    {
        var model = new CapabilityParameterModel(
            RecordingChatClientReturning(GoogleCapabilityIds.GmailMessageRead, "{\"query\":\"is:unread\"}"));

        var payload = await model.ExtractAsync(new CapabilityParameterRequest(
            Descriptor(GoogleCapabilityIds.GmailMessageRead, "Read Gmail messages"),
            "show unread mail"));

        Assert.Equal(GoogleCapabilityIds.GmailMessageRead, payload.ToolId);
        Assert.Equal("is:unread", payload.Arguments.GetProperty("query").GetString());
    }

    [Fact]
    public async Task ExtractAsync_requests_a_json_schema_without_boolean_subschemas()
    {
        var chat = new StaticJsonChatClient("{\"toolId\":\"" + GoogleCapabilityIds.GmailMessageRead + "\",\"arguments\":{}}");
        var model = new CapabilityParameterModel(chat);

        await model.ExtractAsync(new CapabilityParameterRequest(
            Descriptor(GoogleCapabilityIds.GmailMessageRead, "Read Gmail messages"),
            "show unread mail"));

        var format = Assert.IsType<ChatResponseFormatJson>(chat.LastOptions?.ResponseFormat);
        Assert.NotNull(format.Schema);
        var argumentsSchema = format.Schema.Value.GetProperty("properties").GetProperty("arguments");
        Assert.Equal(JsonValueKind.Object, argumentsSchema.ValueKind);
        Assert.Equal("object", argumentsSchema.GetProperty("type").GetString());
        var required = format.Schema.Value.GetProperty("required").EnumerateArray().Select(entry => entry.GetString()!).ToArray();
        Assert.Equal(["toolId", "arguments"], required);
    }

    [Fact]
    public async Task ExtractAsync_uses_the_exact_selected_descriptor_for_guidance()
    {
        var chat = new StaticJsonChatClient("{\"toolId\":\"feature.opaque\",\"arguments\":{}}");
        var model = new CapabilityParameterModel(chat);
        var descriptor = Descriptor("feature.opaque", "Summarize the selected inbox with release-specific behavior");

        await model.ExtractAsync(new CapabilityParameterRequest(descriptor, "summarize my inbox"));

        var guidance = Assert.Single(chat.LastMessages!).Text;
        Assert.Contains(descriptor.Id, guidance, StringComparison.Ordinal);
        Assert.Contains(descriptor.Description, guidance, StringComparison.Ordinal);
    }

    private static CapabilityDescriptor Descriptor(string id, string description) => new(
        id,
        1,
        "Selected capability",
        description,
        [],
        [],
        [],
        CapabilityOrigin.Feature,
        CapabilityOperationKind.InternalWrite,
        true);

    private static IChatClient RecordingChatClientReturning(string toolId, string argumentsJson) =>
        new StaticJsonChatClient($$"""{"toolId":"{{toolId}}","arguments":{{argumentsJson}}}""");

    private sealed class StaticJsonChatClient(string json) : IChatClient
    {
        public ChatOptions? LastOptions { get; private set; }
        public ChatMessage[]? LastMessages { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToArray();
            LastOptions = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, json)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
