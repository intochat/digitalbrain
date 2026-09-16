using ModelContextProtocol.Client;

namespace DigitalBrain.Core;

internal static class McpHttpSession
{
    internal static async Task<T> RunAsync<T>(Uri endpoint, string accessToken,
        Func<McpClient, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + accessToken },
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token).ConfigureAwait(false);
        return await operation(client, timeout.Token).ConfigureAwait(false);
    }

    internal static Task<IList<McpClientTool>> ReadCatalogAsync(Uri endpoint, string accessToken, CancellationToken cancellationToken)
        => RunAsync(endpoint, accessToken,
            static (client, token) => client.ListToolsAsync(cancellationToken: token).AsTask(), cancellationToken);
}
