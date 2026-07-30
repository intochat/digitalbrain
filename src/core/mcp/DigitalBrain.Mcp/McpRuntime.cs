using System.Text.Json;
using DigitalBrain.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Orleans.Journaling;

namespace DigitalBrain.Mcp;

internal sealed class McpRuntime(IMcpClientSessionFactory sessions)
{
    internal const string HttpClientName = "DigitalBrain.Mcp";

    internal async ValueTask<T> RunAsync<T>(
        McpServerDefinition server,
        IDurableValue<byte[]> tokenState,
        Func<ValueTask> commit,
        string durableIdentity,
        CommandId commandId,
        OwnerId owner,
        IGrainFactory grains,
        Func<McpClient, CancellationToken, ValueTask<T>> callback,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(tokenState);
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentException.ThrowIfNullOrWhiteSpace(durableIdentity);
        ArgumentNullException.ThrowIfNull(grains);
        ArgumentNullException.ThrowIfNull(callback);

        var ambient = new McpAuthorizationAmbientState(
            commandId,
            server.Key,
            server.DisplayName,
            owner,
            grains);
        using var scope = McpAuthorizationAmbient.Enter(ambient);

        // Run the MCP open off the grain turn so OAuth's wait on BeginCompleted cannot
        // deadlock against this turn's WhenAny/Begin sequence under Orleans serialization.
        var work = Task.Run(
            () => OpenAndInvokeAsync(
                server,
                tokenState,
                commit,
                durableIdentity,
                ambient,
                callback,
                cancellationToken),
            cancellationToken);

        var step = await Task.WhenAny(work, ambient.SignInReady.Task);
        if (step == ambient.SignInReady.Task && !work.IsCompleted)
        {
            var signIn = await ambient.SignInReady.Task;
            McpAuthorizationCodeHub.RegisterAmbient(signIn.State, ambient);
            var authorization = grains.GetGrain<IMcpAuthorization>(
                NeuronId.For<IMcpAuthorization>(owner, McpAuthorizationNeuron.InstanceName).ToGrainId());
            await authorization.Begin(
                new BeginMcpAuthorization(
                    commandId,
                    server.Key,
                    server.DisplayName,
                    signIn.SignInUrl,
                    signIn.State),
                cancellationToken);
            ambient.BeginCompleted.TrySetResult();
        }

        return await work;
    }

    private async Task<T> OpenAndInvokeAsync<T>(
        McpServerDefinition server,
        IDurableValue<byte[]> tokenState,
        Func<ValueTask> commit,
        string durableIdentity,
        McpAuthorizationAmbientState ambient,
        Func<McpClient, CancellationToken, ValueTask<T>> callback,
        CancellationToken cancellationToken)
    {
        await using var client = await sessions.OpenAsync(
            server,
            tokenState,
            commit,
            durableIdentity,
            cancellationToken,
            ambient);
        return await callback(client, cancellationToken);
    }

    internal static JsonElement RequireStructuredContent(CallToolResult result, McpServerDefinition server, string toolName)
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
