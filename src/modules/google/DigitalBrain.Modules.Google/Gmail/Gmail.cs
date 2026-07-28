using System.Text.Json;
using DigitalBrain.Mcp;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Google;

internal sealed partial class Gmail : Neuron, IGmail
{
    private const string GetMessageName = "get_message";
    private const string TokensName = "google.gmail.oauth";
    private static readonly McpServerDefinition Server = new(
        "google.gmail",
        "DigitalBrain Gmail",
        new Uri("https://gmailmcp.googleapis.com/mcp/v1"),
        "DigitalBrain:Google:Gmail",
        ["https://www.googleapis.com/auth/gmail.readonly"]);
    private readonly McpRuntime _runtime;
    private readonly IDurableValue<byte[]> _tokenState;
    private readonly string _durableIdentity;

    public Gmail(McpRuntime runtime)
    {
        _runtime = runtime;
        _tokenState = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(TokensName);
        _durableIdentity = Id.ToString();
    }

    public async Task<GmailMessage> ReadMessage(string messageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        return await _runtime.RunAsync(
            Server,
            _tokenState,
            () => WriteStateAsync(),
            _durableIdentity,
            async (client, callbackCancellation) =>
            {
                var tools = await client.ListToolsAsync(cancellationToken: callbackCancellation);
                var tool = AdmitGetMessage(tools);
                var result = await tool.CallAsync(
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["messageId"] = messageId,
                        ["messageFormat"] = "FULL_CONTENT",
                    },
                    cancellationToken: callbackCancellation);
                var content = McpRuntime.RequireStructuredContent(result, Server, GetMessageName);
                var responseId = Required(content, "id");

                if (!string.Equals(messageId, responseId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Gmail get_message returned id '{responseId}' for requested message '{messageId}'.");
                }

                return new GmailMessage(
                    responseId,
                    RequiredContent(content, "subject"),
                    Required(content, "sender"),
                    RequiredContent(content, "plaintextBody"));
            },
            cancellationToken);
    }

    private static string Required(JsonElement content, string property)
    {
        if (content.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString()))
        {
            return value.GetString()!;
        }

        throw new InvalidOperationException($"Gmail get_message returned no {property}.");
    }

    private static string RequiredContent(JsonElement content, string property)
    {
        if (content.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString() is { } text
            && (text.Length == 0 || !string.IsNullOrWhiteSpace(text)))
        {
            return text;
        }

        throw new InvalidOperationException($"Gmail get_message returned no {property}.");
    }
}
