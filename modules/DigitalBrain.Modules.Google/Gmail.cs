using System.Text.Json;
using DigitalBrain.Integrations.Mcp;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Google;

internal sealed class Gmail : Neuron, IGmail
{
    private const string TokensName = "google.gmail.oauth";
    private static readonly McpServerDefinition Server = new(
        "google.gmail",
        "DigitalBrain Gmail",
        new Uri("https://gmailmcp.googleapis.com/mcp/v1"),
        "DigitalBrain:Google:Gmail",
        ["https://www.googleapis.com/auth/gmail.readonly"]);
    private static readonly McpToolContract GetMessage = McpToolContract.ReadOnly(
        "get_message",
        new McpToolProperty("messageId", "string"));
    private readonly IMcpClient _client;

    public Gmail(IMcpClientFactory clients)
    {
        _client = clients.Create(
            Server,
            ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(TokensName),
            () => WriteStateAsync(),
            Id.ToString());
    }

    public async Task<GmailMessage> ReadMessageAsync(
        string messageId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        var tool = await _client.InspectAsync(GetMessage, cancellationToken);
        var content = await _client.InvokeAsync(
            tool,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["messageId"] = messageId,
                ["messageFormat"] = "FULL_CONTENT",
            },
            cancellationToken);

        return new GmailMessage(
            Required(content, "id"),
            Required(content, "subject"),
            Required(content, "sender"),
            Required(content, "plaintextBody"));
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
}
