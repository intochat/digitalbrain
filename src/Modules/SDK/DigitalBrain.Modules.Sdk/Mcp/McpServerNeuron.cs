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
    private const string TokensName = "mcp.gateway.oauth";
    private const int FiredRowCap = 200;

    private readonly IDurableValue<byte[]> _tokenState;
    private readonly string _durableIdentity;

    public McpServerNeuron()
    {
        _tokenState = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(TokensName);
        _durableIdentity = Id.ToString();
    }

    public async Task HandleAsync(ListMcpTools synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var server = RequireServer();
        var tools = await ListAsync(server, synapse.CommandId, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await ReplyAsync(new McpToolsListed(synapse.CommandId, [.. tools]), cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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
        var tools = await ListAsync(server, synapse.CommandId, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        var tool = tools.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, synapse.Tool, StringComparison.Ordinal))
            ?? throw new NeuronAuthorizationException(
                $"'{server.DisplayName}' has no tool '{synapse.Tool}'. It has: "
                + string.Join(", ", tools.Select(static candidate => candidate.Name)) + ".");

        if (tool.Destructive)
        {
            throw new NeuronAuthorizationException(
                $"'{tool.Name}' writes to {server.DisplayName}; destructive tools require the "
                + "owner approval flow, which the generic gateway does not carry yet.");
        }

        var rowType = RowTypeOf(synapse.FireRowsAs);
        var content = await CallAsync(server, synapse, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var fired = 0;
        if (rowType is not null)
        {
            foreach (var row in Rows(content).Take(FiredRowCap))
            {
                await EmitAsync(RowSynapse(rowType, row, synapse.FireRowsAs!))
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                fired++;
            }
        }

        await ReplyAsync(
            new McpToolReturned(synapse.CommandId, synapse.Tool, content, fired),
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

    private static Type RowTypeOf(string? fireRowsAs) => fireRowsAs is null
        ? null!
        : SynapseTypeIndex.FindByAlias(fireRowsAs)
            ?? throw new NeuronAuthorizationException(
                $"'{fireRowsAs}' names no synapse; rows can only fire as a known contract.");

    private static Synapse RowSynapse(Type rowType, JsonElement row, string fireRowsAs)
    {
        Synapse? shaped;
        try
        {
            shaped = JsonSerializer.Deserialize(row, rowType, RowShaping) as Synapse;
        }
        catch (JsonException misshapen)
        {
            throw new NeuronAuthorizationException(
                $"A result row does not fit '{fireRowsAs}' "
                + $"({ContractSignature.Of(rowType)}): {misshapen.Message} "
                + "Shape the query so its columns match those fields.");
        }

        return shaped
            ?? throw new NeuronAuthorizationException(
                $"A result row produced no '{fireRowsAs}' value.");
    }

    private static readonly JsonSerializerOptions RowShaping = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // Tabular MCP results arrive as a root array or as the first array-valued
    // property (records, rows, items — servers differ).
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
        Func<McpClient, CancellationToken, ValueTask<TResult>> session,
        CancellationToken cancellationToken)
    {
        await McpAuthorizationRail.EnsureAuthorizedAsync(
            GrainFactory,
            Id.Owner,
            ServiceProvider,
            TimeProvider,
            commandId,
            server,
            _tokenState,
            () => WriteStateAsync(),
            _durableIdentity,
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        return await McpClientSessions.RunAsync(
            server,
            ServiceProvider,
            _tokenState,
            () => WriteStateAsync(),
            _durableIdentity,
            commandId,
            Id.Owner,
            GrainFactory,
            session,
            cancellationToken).ConfigureAwait(true);
    }
}
