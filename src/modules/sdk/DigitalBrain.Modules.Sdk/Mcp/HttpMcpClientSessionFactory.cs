using DigitalBrain.Security;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
using Orleans.Journaling;

namespace DigitalBrain.Modules.Sdk.Mcp;
internal sealed class HttpMcpClientSessionFactory(
    IConfiguration configuration,
    IHttpClientFactory httpClients,
    IDurablePayloadProtector protector) : IMcpClientSessionFactory
{
    public async ValueTask<McpClient> OpenAsync(
        McpServerDefinition server,
        IDurableValue<byte[]> tokenState,
        Func<ValueTask> commit,
        string durableIdentity,
        CancellationToken cancellationToken,
        McpAuthorizationAmbientState? ambient = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(tokenState);
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentException.ThrowIfNullOrWhiteSpace(durableIdentity);

        var tokens = new DurableMcpTokenCache(
            tokenState,
            commit,
            protector,
            McpTokenPresence.Purpose(server.Key, durableIdentity));
        var authorization = McpOAuthOptions.Create(
            server,
            configuration,
            tokens,
            ambient ?? McpAuthorizationAmbient.State);
        var httpClient = httpClients.CreateClient(McpRuntime.HttpClientName);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = server.Endpoint,
                Name = server.DisplayName,
                OAuth = authorization,
            },
            httpClient,
            loggerFactory: null,
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
