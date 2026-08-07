using ModelContextProtocol.Client;
using Orleans.Journaling;

namespace DigitalBrain.Modules.Sdk.Mcp;

internal interface IMcpClientSessionFactory
{
    ValueTask<McpClient> OpenAsync(
        McpServerDefinition server,
        IDurableValue<byte[]> tokenState,
        Func<ValueTask> commit,
        string durableIdentity,
        CancellationToken cancellationToken,
        McpAuthorizationAmbientState? ambient = null);
}
