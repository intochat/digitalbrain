using System.Text.Json;
using ModelContextProtocol.Authentication;

namespace DigitalBrain.Google;

internal interface IGoogleMcpAuthorization
{
    ClientOAuthOptions CreateOptions();
}

internal interface IGmailMcpTransport
{
    ValueTask<JsonElement> CallToolAsync(
        Uri endpoint,
        ClientOAuthOptions authorization,
        string tool,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken);
}
