using System.Text.Json;
using DigitalBrain.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Orleans.Journaling;

namespace DigitalBrain.Mcp;

internal sealed class McpRuntime(IMcpClientSessionFactory sessions)
{
    internal const string HttpClientName = "DigitalBrain.Mcp";

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using 'IDisposable' instances complete before the instances are disposed",
        Justification = "On no-code auth outcomes the MCP SDK CreateAsync may never complete; the open token is canceled and the parked Task.Run is abandoned so the grain turn can return.")]
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
        // Register by command id immediately so deny/cancel can always AbortOpen this ambient.
        McpAuthorizationCodeHub.RegisterAmbient(commandId.ToString(), ambient);
        using var scope = McpAuthorizationAmbient.Enter(ambient);
        using var openLinked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            ambient.OpenCancellation);
        var openToken = openLinked.Token;

        // MCP open off the grain turn so OAuth's wait on BeginCompleted cannot deadlock against
        // this turn's WhenAny/Begin sequence under Orleans serialization.
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

            var step = await Task.WhenAny(work, ambient.SignInReady.Task).WaitAsync(cancellationToken);
            if (ReferenceEquals(step, ambient.SignInReady.Task) && !work.IsCompleted)
            {
                var signIn = await ambient.SignInReady.Task;
                McpAuthorizationCodeHub.RegisterAmbient(signIn.State, ambient);
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

            // Race hold-open work against any no-code terminal (deny, cancel, abandon).
            // On terminal, cancel and abandon the parked CreateAsync — awaiting it after a null
            // AuthorizationResult can hang forever on MCP SDK session Completion.
            var settled = await Task.WhenAny(work, ambient.Terminal.Task).WaitAsync(cancellationToken);
            if (ReferenceEquals(settled, ambient.Terminal.Task) && !work.IsCompleted)
            {
                ambient.AbortOpen();
                throw new McpAuthorizationDeniedException(
                    $"Authorization for '{server.Key}' ended without a code for command '{commandId}'.");
            }

            return await work;
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
