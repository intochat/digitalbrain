using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Orleans.Journaling;

namespace DigitalBrain.Integrations.Mcp;

internal sealed class McpRuntime(IMcpClientSessionFactory sessions)
{
    internal const string HttpClientName = "DigitalBrain.Integrations.Mcp";

    internal async ValueTask<T> RunAsync<T>(
        McpServerDefinition server,
        IDurableValue<byte[]> tokenState,
        Func<ValueTask> commit,
        string durableIdentity,
        Func<McpClient, CancellationToken, ValueTask<T>> callback,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(tokenState);
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentException.ThrowIfNullOrWhiteSpace(durableIdentity);
        ArgumentNullException.ThrowIfNull(callback);

        await using var session = await sessions.OpenAsync(
            server,
            tokenState,
            commit,
            durableIdentity,
            cancellationToken);
        return await callback(session.Client, cancellationToken);
    }

    internal static JsonElement RequireStructuredContent(
        CallToolResult result,
        McpServerDefinition server,
        string toolName)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        if (result.IsError is true)
        {
            throw new InvalidOperationException(
                $"{server.DisplayName} MCP tool '{toolName}' reported an error.");
        }

        return result.StructuredContent?.Clone()
            ?? throw new InvalidOperationException(
                $"{server.DisplayName} MCP tool '{toolName}' returned no structured content.");
    }
}
