using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using Orleans.Journaling;

namespace DigitalBrain.Modules.Sdk.Mcp;

[GrainType("mcp")]
public sealed class McpServerNeuron : Neuron, IMcp
{
    private const string TokensName = "mcp.gateway.oauth.principals";
    private const int FiredRowCap = 200;

    private readonly IDurableDictionary<string, byte[]> _principalTokens;

    public McpServerNeuron()
    {
        _principalTokens = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<string, byte[]>>(TokensName);
    }

    public async Task HandleAsync(ListMcpTools synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var server = RequireServer();
        var tools = await ListAsync(server, synapse.CommandId, synapse.Actor, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await ReplyAsync(new McpToolsListed(synapse.CommandId, [.. tools]), cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public Task HandleAsync(ListMcpServers synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        // Durable catalog is DI-registered McpServerDefinition instances (module-owned).
        // Per-server grain instance answers with the full known registry for discovery.
        var servers = ServiceProvider.GetServices<McpServerDefinition>()
            .OrderBy(static s => s.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static s => new McpServerInfo(
                s.Key,
                s.DisplayName,
                s.Endpoint.AbsoluteUri,
                [.. s.Scopes]))
            .ToArray();

        return ReplyAsync(new McpServersListed(synapse.CommandId, servers), cancellationToken);
    }

    public async Task HandleAsync(CallMcpTool synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(synapse.Tool))
        {
            throw new NeuronAuthorizationException(
                $"'{Id}' needs a tool name; fire db.mcp.list-tools to see the catalog.");
        }

        var server = RequireServer();
        var tools = await ListAsync(server, synapse.CommandId, synapse.Actor, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        var tool = tools.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, synapse.Tool, StringComparison.Ordinal))
            ?? throw new NeuronAuthorizationException(
                $"'{server.DisplayName}' has no tool '{synapse.Tool}'. It has: "
                + string.Join(", ", tools.Select(static candidate => candidate.Name)) + ".");

        // S18: destructive tools are callable but require an explicit confirm press (not a ban).
        if (tool.Destructive && !synapse.ConfirmDestructive)
        {
            throw new NeuronAuthorizationException(
                $"'{server.DisplayName}' tool '{tool.Name}' is destructive. "
                + "Re-fire db.mcp.call-tool with ConfirmDestructive=true after the owner presses once.");
        }

        var rowType = RowTypeOf(synapse.FireRowsAs);
        string? integrationSubject = null;
        JsonElement content;
        try
        {
            content = await CallAsync(server, synapse, cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            if (synapse.Actor is { } actor)
            {
                integrationSubject = McpTokenPresence.SubjectKey(actor);
            }
        }
        catch (Exception)
        {
            // Settled refusals and transport errors are already journaled via the inbound
            // CallMcpTool (actor stamp) — rethrow without token leakage.
            throw;
        }

        var fired = 0;
        var truncated = false;
        var rowsAvailable = 0;
        string? summary = null;
        JsonElement replyContent = content;

        if (rowType is not null)
        {
            var allRows = Rows(content).ToArray();
            rowsAvailable = allRows.Length;
            truncated = rowsAvailable > FiredRowCap;
            var batch = allRows.Take(FiredRowCap).ToArray();

            // S11/S20: validate the WHOLE batch before the first emit — atomic refuse.
            var shaped = new Synapse[batch.Length];
            for (var i = 0; i < batch.Length; i++)
            {
                shaped[i] = RowSynapse(rowType, batch[i], synapse.FireRowsAs!, rowIndex: i);
            }

            foreach (var row in shaped)
            {
                await EmitAsync(row).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                fired++;
            }

            summary = truncated
                ? $"Fired {fired} of {rowsAvailable} rows as '{synapse.FireRowsAs}' (cap {FiredRowCap}; truncated)."
                : $"Fired {fired} rows as '{synapse.FireRowsAs}'.";
            replyContent = JsonSerializer.SerializeToElement(new
            {
                summary,
                firedRows = fired,
                rowsAvailable,
                truncated,
                fireRowsAs = synapse.FireRowsAs,
            });
        }

        await ReplyAsync(
            new McpToolReturned(
                synapse.CommandId,
                synapse.Tool,
                replyContent,
                fired,
                synapse.Actor,
                integrationSubject,
                truncated,
                rowsAvailable,
                summary),
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private McpServerDefinition RequireServer()
    {
        var known = ServiceProvider.GetServices<McpServerDefinition>().ToArray();

        return known.FirstOrDefault(server =>
                string.Equals(server.Key, Id.Name, StringComparison.Ordinal))
            ?? throw new NeuronAuthorizationException(
                $"'{Id}' matches no configured MCP server. Configured: "
                + (known.Length == 0
                    ? "none"
                    : string.Join(", ", known.Select(static server => server.Key)))
                + ".");
    }

    private static Type? RowTypeOf(string? fireRowsAs) => fireRowsAs is null
        ? null
        : SynapseTypeIndex.FindByAlias(fireRowsAs)
            ?? throw new NeuronAuthorizationException(
                $"'{fireRowsAs}' names no synapse; rows can only fire as a known contract.");

    private static Synapse RowSynapse(Type rowType, JsonElement row, string fireRowsAs, int rowIndex = 0)
    {
        Synapse? shaped;
        try
        {
            shaped = JsonSerializer.Deserialize(row, rowType, RowShaping) as Synapse;
        }
        catch (JsonException misshapen)
        {
            throw new NeuronAuthorizationException(
                $"Result row {rowIndex} does not fit '{fireRowsAs}' "
                + $"({ContractSignature.Of(rowType)}): {misshapen.Message} "
                + "No rows were fired. Shape the query so every column matches those fields.");
        }

        return shaped
            ?? throw new NeuronAuthorizationException(
                $"Result row {rowIndex} produced no '{fireRowsAs}' value. No rows were fired.");
    }

    private static readonly JsonSerializerOptions RowShaping = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static IEnumerable<JsonElement> Rows(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.Array)
        {
            return content.EnumerateArray().ToArray();
        }

        if (content.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in content.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    return property.Value.EnumerateArray().ToArray();
                }
            }
        }

        return [];
    }

    private async Task<IReadOnlyList<McpToolDescription>> ListAsync(
        McpServerDefinition server,
        CommandId commandId,
        ActorContext? actor,
        CancellationToken cancellationToken)
    {
        if (ServiceProvider.GetService<IMcpToolTransport>() is { } transport)
        {
            return await transport.ListToolsAsync(server, cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        return await AuthorizedAsync(
            server,
            commandId,
            actor,
            static async ValueTask<IReadOnlyList<McpToolDescription>> (client, callbackCancellation) =>
            {
                var listed = await client.ListToolsAsync(cancellationToken: callbackCancellation)
                    .ConfigureAwait(true);
                return
                [
                    .. listed.Select(static tool => new McpToolDescription(
                        tool.Name,
                        tool.Description ?? string.Empty,
                        tool.ProtocolTool.InputSchema.GetRawText(),
                        tool.ProtocolTool.Annotations?.DestructiveHint ?? true)),
                ];
            },
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task<JsonElement> CallAsync(
        McpServerDefinition server,
        CallMcpTool synapse,
        CancellationToken cancellationToken)
    {
        if (ServiceProvider.GetService<IMcpToolTransport>() is { } transport)
        {
            return await transport.CallToolAsync(server, synapse.Tool, synapse.Arguments, cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        return await AuthorizedAsync(
            server,
            synapse.CommandId,
            synapse.Actor,
            async ValueTask<JsonElement> (client, callbackCancellation) =>
            {
                var arguments = synapse.Arguments.ValueKind == JsonValueKind.Object
                    ? synapse.Arguments.EnumerateObject()
                        .ToDictionary(
                            static property => property.Name,
                            static property => (object?)property.Value,
                            StringComparer.Ordinal)
                    : new Dictionary<string, object?>(StringComparer.Ordinal);
                var listed = await client.ListToolsAsync(cancellationToken: callbackCancellation)
                    .ConfigureAwait(true);
                var tool = listed.First(candidate =>
                    string.Equals(candidate.Name, synapse.Tool, StringComparison.Ordinal));
                var result = await tool.CallAsync(arguments, cancellationToken: callbackCancellation)
                    .ConfigureAwait(true);

                return McpClientSessions.RequireStructuredContent(result, server, synapse.Tool);
            },
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task<TResult> AuthorizedAsync<TResult>(
        McpServerDefinition server,
        CommandId commandId,
        ActorContext? actor,
        Func<McpClient, CancellationToken, ValueTask<TResult>> session,
        CancellationToken cancellationToken)
    {
        if (actor is null)
        {
            throw new NeuronAuthorizationException(
                $"'{server.DisplayName}' requires an authenticated actor on db.mcp.* before calling tools. "
                + "Sign in, then fire with Actor set to the verified principal.");
        }

        var subjectKey = McpTokenPresence.SubjectKey(actor);
        var tokenSlot = new PrincipalTokenSlot(_principalTokens, subjectKey);

        await McpAuthorizationRail.EnsureAuthorizedAsync(
            GrainFactory,
            Id.Owner,
            ServiceProvider,
            TimeProvider,
            commandId,
            server,
            tokenSlot,
            () => WriteStateAsync(),
            actor,
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        return await McpClientSessions.RunAsync(
            server,
            ServiceProvider,
            tokenSlot,
            () => WriteStateAsync(),
            actor,
            commandId,
            Id.Owner,
            GrainFactory,
            session,
            cancellationToken).ConfigureAwait(true);
    }
}
