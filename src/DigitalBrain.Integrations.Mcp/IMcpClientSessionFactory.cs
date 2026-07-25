using ModelContextProtocol.Client;
using Orleans.Journaling;

namespace DigitalBrain.Integrations.Mcp;

internal interface IMcpClientSession : IAsyncDisposable
{
    McpClient Client { get; }
}

internal interface IMcpClientSessionFactory
{
    ValueTask<IMcpClientSession> OpenAsync(
        McpServerDefinition server,
        IDurableValue<byte[]> tokenState,
        Func<ValueTask> commit,
        string durableIdentity,
        CancellationToken cancellationToken);
}
