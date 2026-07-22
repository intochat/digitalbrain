using System.Text.Json;
using ModelContextProtocol.Authentication;

namespace DigitalBrain.Salesforce;

internal interface ISalesforceMcpAuthorization
{
    ClientOAuthOptions CreateOptions(ITokenCache tokenCache);
}

internal interface ISalesforceMcpTransport
{
    ValueTask<McpToolSnapshot> ReadToolAsync(
        Uri endpoint,
        ClientOAuthOptions authorization,
        string tool,
        CancellationToken cancellationToken);

    ValueTask<JsonElement> CallToolAsync(
        Uri endpoint,
        ClientOAuthOptions authorization,
        string tool,
        IReadOnlyDictionary<string, object?> arguments,
        string expectedSchemaFingerprint,
        CancellationToken cancellationToken);
}
