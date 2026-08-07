using System.Text.Json;
using DigitalBrain.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Orleans.Journaling;

namespace DigitalBrain.Modules.Sdk.Mcp;

public sealed class McpRuntime
{
    internal const string HttpClientName = "DigitalBrain.Mcp";

    private readonly IMcpClientSessionFactory _sessions;

    internal McpRuntime(IMcpClientSessionFactory sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        _sessions = sessions;
    }
    public async ValueTask<T> RunAsync<T>(
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

        McpAuthorizationCodeHub.RegisterAmbient(commandId.ToString(), ambient);
        using var scope = McpAuthorizationAmbient.Enter(ambient);
        using var openLinked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            ambient.OpenCancellation);
        var openToken = openLinked.Token;

        var work = Task.Run(
            () => OpenAndInvokeAsync(
                server,
                tokenState,
                commit,
                durableIdentity,
                ambient,
                callback,
                openToken),
            openToken);

        try
        {
            var authorization = grains.GetGrain<IMcpAuthorization>(
                NeuronId.For<IMcpAuthorization>(owner, McpAuthorizationNeuron.InstanceName).ToGrainId());

            var step = await Task.WhenAny(work, ambient.SignInReady.Task).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (ReferenceEquals(step, ambient.SignInReady.Task) && !work.IsCompleted)
            {
                var signIn = await ambient.SignInReady.Task.ConfigureAwait(false);
                McpAuthorizationCodeHub.RegisterAmbient(signIn.State, ambient);
                await authorization.Begin(
                    new BeginMcpAuthorization(
                        commandId,
                        server.Key,
                        server.DisplayName,
                        signIn.SignInUrl,
                        signIn.State),
                    cancellationToken).ConfigureAwait(false);
                ambient.BeginCompleted.TrySetResult();
            }

            var settled = await Task.WhenAny(work, ambient.Terminal.Task).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (ReferenceEquals(settled, ambient.Terminal.Task) && !work.IsCompleted)
            {
                ambient.AbortOpen();
                throw new McpAuthorizationDeniedException(
                    $"Authorization for '{server.Key}' ended without a code for command '{commandId}'.");
            }

            return await work.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested
            || ambient.OpenCancellation.IsCancellationRequested)
        {
            ambient.AbortOpen();
            McpAuthorizationCodeHub.AbortOpen(commandId);
            throw;
        }
        finally
        {
            ambient.AbortOpen();
            McpAuthorizationCodeHub.UnregisterAmbient(ambient);
        }
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
        var client = await _sessions.OpenAsync(
            server,
            tokenState,
            commit,
            durableIdentity,
            cancellationToken,
            ambient).ConfigureAwait(false);
        await using (client.ConfigureAwait(false))
        {
            return await callback(client, cancellationToken).ConfigureAwait(false);
        }
    }

    public static JsonElement RequireStructuredContent(CallToolResult result, McpServerDefinition server, string toolName)
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
