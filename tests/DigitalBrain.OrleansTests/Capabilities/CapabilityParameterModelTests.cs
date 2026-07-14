using System.Text.Json;
using DigitalBrain.Integrations.Google;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Integrations.Salesforce;
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
        var model = new CapabilityParameterModel(chat, Catalog());

        await Assert.ThrowsAsync<InvalidOperationException>(() => model.ExtractAsync(new CapabilityParameterRequest(
            GoogleCapabilityIds.GmailMessageRead,
            "list recent mail")));
    }

    [Fact]
    public async Task ExtractAsync_rejects_an_unknown_capability()
    {
        var model = new CapabilityParameterModel(RecordingChatClientReturning(GoogleCapabilityIds.GmailMessageRead, "{}"), Catalog());

        await Assert.ThrowsAsync<ArgumentException>(() => model.ExtractAsync(new CapabilityParameterRequest(
            "not.a.capability",
            "list recent mail")));
    }

    [Fact]
    public async Task ExtractAsync_returns_the_server_selected_capability_with_the_extracted_arguments()
    {
        var model = new CapabilityParameterModel(
            RecordingChatClientReturning(GoogleCapabilityIds.GmailMessageRead, "{\"query\":\"is:unread\"}"),
            Catalog());

        var payload = await model.ExtractAsync(new CapabilityParameterRequest(
            GoogleCapabilityIds.GmailMessageRead,
            "show unread mail"));

        Assert.Equal(GoogleCapabilityIds.GmailMessageRead, payload.ToolId);
        Assert.Equal("is:unread", payload.Arguments.GetProperty("query").GetString());
    }

    [Fact]
    public async Task ExtractAsync_requests_a_json_schema_without_boolean_subschemas()
    {
        var chat = new StaticJsonChatClient("{\"toolId\":\"" + GoogleCapabilityIds.GmailMessageRead + "\",\"arguments\":{}}");
        var model = new CapabilityParameterModel(chat, Catalog());

        await model.ExtractAsync(new CapabilityParameterRequest(
            GoogleCapabilityIds.GmailMessageRead,
            "show unread mail"));

        var format = Assert.IsType<ChatResponseFormatJson>(chat.LastOptions?.ResponseFormat);
        Assert.NotNull(format.Schema);
        var argumentsSchema = format.Schema.Value.GetProperty("properties").GetProperty("arguments");
        Assert.Equal(JsonValueKind.Object, argumentsSchema.ValueKind);
        Assert.Equal("object", argumentsSchema.GetProperty("type").GetString());
        var required = format.Schema.Value.GetProperty("required").EnumerateArray().Select(entry => entry.GetString()).ToArray();
        Assert.Equal(["toolId", "arguments"], required);
    }

    private static BuiltInCapabilityCatalog Catalog() =>
        new([new GoogleCapabilityDescriptorSource(), new SalesforceCapabilityDescriptorSource()]);

    private static IChatClient RecordingChatClientReturning(string toolId, string argumentsJson) =>
        new StaticJsonChatClient($$"""{"toolId":"{{toolId}}","arguments":{{argumentsJson}}}""");

    private sealed class StaticJsonChatClient(string json) : IChatClient
    {
        public ChatOptions? LastOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
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
