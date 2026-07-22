using System.Text.Json;
using ModelContextProtocol.Authentication;

namespace DigitalBrain.Salesforce;

internal interface ISalesforceMcpAuthorization
{
    ClientOAuthOptions CreateOptions();
}

internal interface ISalesforceMcpTransport
{
    ValueTask<JsonElement> CallToolAsync(
        Uri endpoint,
        ClientOAuthOptions authorization,
        string tool,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken);
}
