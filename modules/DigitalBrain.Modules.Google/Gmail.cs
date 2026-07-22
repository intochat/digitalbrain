using System.Text.Json;
using DigitalBrain.Kernel;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Authentication;
using Orleans.Journaling;

namespace DigitalBrain.Google;

internal sealed class Gmail : Neuron, IGmail
{
    private const string TokensName = "google.gmail.oauth";
    private static readonly Uri Endpoint = new("https://gmailmcp.googleapis.com/mcp/v1");
    private readonly IGoogleMcpAuthorization _authorization;
    private readonly IGmailMcpTransport _transport;
    private readonly ITokenCache _tokens;

    public Gmail(
        IGoogleMcpAuthorization authorization,
        IGmailMcpTransport transport)
    {
        _authorization = authorization;
        _transport = transport;
        _tokens = new DurableMcpTokenCache(
            ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(TokensName),
            () => WriteStateAsync(),
            ServiceProvider.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("DigitalBrain.Google.Gmail.OAuth"));
    }

    public async Task<GmailMessage> ReadMessageAsync(
        string messageId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        var authorization = _authorization.CreateOptions(_tokens);
        var tool = await _transport.ReadToolAsync(
            Endpoint,
            authorization,
            "get_message",
            cancellationToken);
        var content = await _transport.CallToolAsync(
            Endpoint,
            authorization,
            "get_message",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["messageId"] = messageId,
                ["messageFormat"] = "FULL_CONTENT",
            },
            tool.SchemaFingerprint,
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
