using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Orleans.Journaling;

namespace DigitalBrain.Modules.Sdk.Mcp;

public static class McpClientSessions
{
    public const string HttpClientName = "DigitalBrain.Mcp";

    public static async ValueTask<T> RunAsync<T>(
        McpServerDefinition server,
        IServiceProvider services,
        PrincipalTokenSlot tokenSlot,
        Func<ValueTask> commit,
        ActorContext actor,
        CommandId commandId,
        OwnerId owner,
        IGrainFactory grains,
        Func<McpClient, CancellationToken, ValueTask<T>> callback,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tokenSlot);
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(grains);
        ArgumentNullException.ThrowIfNull(callback);

        var configuration = services.GetRequiredService<IConfiguration>();
        var protector = services.GetRequiredService<IDurablePayloadProtector>();
        var httpClients = services.GetRequiredService<IHttpClientFactory>();
        var purpose = McpTokenPresence.UserIntegration(server.Key, actor, [.. server.Scopes])
            .ProtectedTokenReference;
        var session = new McpOAuthSession(commandId, server.Key, server.DisplayName, owner, grains, actor);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Cancellation);
        var token = linked.Token;

        try
        {
            var tokens = new DurableMcpTokenCache(tokenSlot, commit, protector, purpose);
            var logger = services.GetService<ILoggerFactory>()?.CreateLogger("DigitalBrain.Mcp.OAuth");
            var oauth = McpOAuthOptions.Create(server, configuration, tokens, session, logger);
            var httpClient = httpClients.CreateClient(HttpClientName);
            var transport = new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = server.Endpoint,
                    Name = server.DisplayName,
                    OAuth = oauth,
                },
                httpClient,
                loggerFactory: null,
                ownsHttpClient: true);

            var client = await McpClient
                .CreateAsync(transport, cancellationToken: token)
                .ConfigureAwait(true);
            await using (client.ConfigureAwait(true))
            {
                return await callback(client, token).ConfigureAwait(true);
            }
        }
        finally
        {
            session.Cancel();
            McpAuthorizationCodeHub.UnregisterSession(session);
        }
    }

    public static JsonElement RequireStructuredContent(
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
