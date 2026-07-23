using System.Text.Json;
using DigitalBrain.Security;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using Orleans.Journaling;

namespace DigitalBrain.Integrations.Mcp;

internal interface IMcpClientFactory
{
    IMcpClient Create(
        McpServerDefinition server,
        IDurableValue<byte[]> tokenState,
        Func<ValueTask> commit,
        string owner);
}

internal interface IMcpClient
{
    ValueTask<McpToolHandle> InspectAsync(
        McpToolContract contract,
        CancellationToken cancellationToken);

    ValueTask<JsonElement> InvokeAsync(
        McpToolHandle tool,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken);
}

internal sealed class SdkMcpClientFactory(
    IConfiguration configuration,
    IHttpClientFactory httpClients,
    IDurablePayloadProtector protector,
    IMcpAuthorizationRedirect authorizationRedirect) : IMcpClientFactory
{
    public IMcpClient Create(
        McpServerDefinition server,
        IDurableValue<byte[]> tokenState,
        Func<ValueTask> commit,
        string owner)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(tokenState);
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        var tokens = new DurableMcpTokenCache(
            tokenState,
            commit,
            protector,
            $"mcp/oauth/{server.Key}/{owner}");
        var oauth = McpOAuthOptions.Create(
            server,
            configuration,
            tokens,
            authorizationRedirect);
        return new SdkMcpClient(server, oauth, httpClients);
    }
}

internal sealed class SdkMcpClient(
    McpServerDefinition server,
    ClientOAuthOptions authorization,
    IHttpClientFactory httpClients) : IMcpClient
{
    internal const string HttpClientName = "DigitalBrain.Integrations.Mcp";

    public async ValueTask<McpToolHandle> InspectAsync(
        McpToolContract contract,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contract);

        await using var session = await OpenAsync(cancellationToken);
        var snapshot = await ReadAsync(session.Client, contract, cancellationToken);
        contract.Admit(snapshot, server.DisplayName);
        return new McpToolHandle(contract, snapshot.SchemaFingerprint);
    }

    public async ValueTask<JsonElement> InvokeAsync(
        McpToolHandle tool,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(arguments);

        await using var session = await OpenAsync(cancellationToken);
        var snapshot = await ReadAsync(session.Client, tool.Contract, cancellationToken);

        if (!string.Equals(snapshot.SchemaFingerprint, tool.SchemaFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{server.DisplayName} MCP tool '{tool.Contract.Name}' schema changed after admission.");
        }

        tool.Contract.Admit(snapshot, server.DisplayName);
        var result = await session.Client.CallToolAsync(
            tool.Contract.Name,
            arguments,
            cancellationToken: cancellationToken);

        if (result.IsError is true)
        {
            throw new InvalidOperationException(
                $"{server.DisplayName} MCP tool '{tool.Contract.Name}' reported an error.");
        }

        return result.StructuredContent?.Clone()
            ?? throw new InvalidOperationException(
                $"{server.DisplayName} MCP tool '{tool.Contract.Name}' returned no structured content.");
    }

    private async ValueTask<McpToolSnapshot> ReadAsync(
        ModelContextProtocol.Client.McpClient client,
        McpToolContract contract,
        CancellationToken cancellationToken)
    {
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        var matches = tools
            .Where(candidate => string.Equals(candidate.Name, contract.Name, StringComparison.Ordinal))
            .ToArray();

        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"{server.DisplayName} MCP must advertise required tool '{contract.Name}' exactly once.");
        }

        var tool = matches[0];
        return McpToolSnapshot.Create(
            tool.Name,
            tool.ProtocolTool.InputSchema,
            tool.ProtocolTool.Annotations?.ReadOnlyHint,
            tool.ProtocolTool.Annotations?.DestructiveHint);
    }

    private async ValueTask<McpSession> OpenAsync(CancellationToken cancellationToken)
    {
        var httpClient = httpClients.CreateClient(HttpClientName);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = server.Endpoint,
                Name = server.DisplayName,
                OAuth = authorization,
            },
            httpClient);

        try
        {
            var client = await ModelContextProtocol.Client.McpClient.CreateAsync(
                transport,
                cancellationToken: cancellationToken);
            return new McpSession(httpClient, transport, client);
        }
        catch
        {
            await transport.DisposeAsync();
            httpClient.Dispose();
            throw;
        }
    }

    private sealed class McpSession(
        HttpClient httpClient,
        HttpClientTransport transport,
        ModelContextProtocol.Client.McpClient client) : IAsyncDisposable
    {
        internal ModelContextProtocol.Client.McpClient Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await transport.DisposeAsync();
            httpClient.Dispose();
        }
    }
}
