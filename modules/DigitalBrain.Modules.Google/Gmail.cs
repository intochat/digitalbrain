using System.Text.Json;
using DigitalBrain.Kernel;

namespace DigitalBrain.Google;

internal sealed class Gmail(
    IGoogleMcpAuthorization authorization,
    IGmailMcpTransport transport) : Neuron, IGmail
{
    private static readonly Uri Endpoint = new("https://gmailmcp.googleapis.com/mcp/v1");

    public async Task<GmailMessage> ReadMessageAsync(string messageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        var content = await transport.CallToolAsync(
            Endpoint,
            authorization.CreateOptions(),
            "get_message",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["messageId"] = messageId,
                ["messageFormat"] = "FULL_CONTENT",
            },
            CancellationToken.None);

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
