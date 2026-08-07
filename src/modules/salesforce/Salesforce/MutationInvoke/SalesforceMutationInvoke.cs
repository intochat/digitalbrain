using System.Text.Json;
using DigitalBrain.Modules.Sdk.Mcp;
using ModelContextProtocol.Client;

namespace DigitalBrain.Salesforce;

internal sealed partial class Salesforce
{
    private async Task<MutationData> InvokeUpdateAsync(MutationData mutation, CancellationToken cancellationToken)
    {
        await McpAuthorizationRail.EnsureAuthorizedAsync(
            GrainFactory,
            Id.Owner,
            ServiceProvider,
            TimeProvider,
            mutation.CommandId,
            Server,
            _tokenState,
            () => WriteStateAsync(),
            _durableIdentity,
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        return await _runtime.RunAsync(
            Server,
            _tokenState,
            () => WriteStateAsync(),
            _durableIdentity,
            mutation.CommandId,
            Id.Owner,
            GrainFactory,
            async (client, callbackCancellation) =>
            {
                var tools = await client.ListToolsAsync(cancellationToken: callbackCancellation).ConfigureAwait(true);
                var updateTool = SelectUpdateTool(tools);

                if (!string.Equals(
                    updateTool.Fingerprint,
                    RequiredFingerprint(mutation.UpdateSchemaFingerprint, UpdateAccountName),
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{Server.DisplayName} MCP tool '{UpdateAccountName}' schema changed after admission.");
                }

                var result = await updateTool.Tool.CallAsync(
                    UpdateArguments(mutation),
                    cancellationToken: callbackCancellation).ConfigureAwait(true);
                var content = McpRuntime.RequireStructuredContent(result, Server, UpdateAccountName);

                return mutation with
                {
                    Status = IsSuccessfulUpdate(content)
                        ? MutationStatus.Completed
                        : MutationStatus.Invoking,
                };
            },
            cancellationToken).ConfigureAwait(true);
    }

    private static Dictionary<string, object?> UpdateArguments(MutationData mutation)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["sobject-name"] = "Account",
            ["id"] = mutation.AccountId,
            ["body"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Description"] = mutation.Description,
            },
        };

    private static bool IsSuccessfulUpdate(JsonElement content) =>
        content.ValueKind is JsonValueKind.Object
        && content.TryGetProperty("success", out var success)
        && success.ValueKind is JsonValueKind.True;

    private async Task<MutationData> ReconcileBoundedAsync(MutationData mutation)
    {
        using var reconciliation = new CancellationTokenSource(ReconciliationTimeout);
        return await ReconcileAsync(mutation, reconciliation.Token).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }
    private async Task<MutationData> ReconcileAsync(MutationData mutation, CancellationToken cancellationToken)
    {
        try
        {
            await McpAuthorizationRail.EnsureAuthorizedAsync(
                GrainFactory,
                Id.Owner,
                ServiceProvider,
                TimeProvider,
                mutation.CommandId,
                Server,
                _tokenState,
                () => WriteStateAsync(),
                _durableIdentity,
                cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            var content = await _runtime.RunAsync(
                Server,
                _tokenState,
                () => WriteStateAsync(),
                _durableIdentity,
                mutation.CommandId,
                Id.Owner,
                GrainFactory,
                async (client, callbackCancellation) =>
                {
                    var tools = await client.ListToolsAsync(cancellationToken: callbackCancellation).ConfigureAwait(true);
                    var queryTool = SelectQueryTool(tools);

                    if (!string.Equals(
                        queryTool.Fingerprint,
                        RequiredFingerprint(mutation.QuerySchemaFingerprint, QueryAccountName),
                        StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"{Server.DisplayName} MCP tool '{QueryAccountName}' schema changed after admission.");
                    }

                    var result = await queryTool.Tool.CallAsync(
                        QueryArguments(mutation),
                        cancellationToken: callbackCancellation).ConfigureAwait(true);
                    return McpRuntime.RequireStructuredContent(result, Server, QueryAccountName);
                },
                cancellationToken).ConfigureAwait(true);

            return mutation with
            {
                Status = ReconciliationMatches(content, mutation)
                    ? MutationStatus.Completed
                    : MutationStatus.OutcomeUncertain,
            };
        }
        catch (Exception)
        {
            return mutation with { Status = MutationStatus.OutcomeUncertain };
        }
    }

    private static Dictionary<string, object?> QueryArguments(MutationData mutation)
    {
        ValidateAccountId(mutation.AccountId);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["query"] = $"SELECT Id, Description FROM Account WHERE Id = '{mutation.AccountId}' LIMIT 1",
        };
    }

    private static bool ReconciliationMatches(JsonElement content, MutationData mutation)
    {
        var records = content;

        if (content.ValueKind is JsonValueKind.Object
            && content.TryGetProperty("records", out var nested))
        {
            records = nested;
        }

        if (records.ValueKind is not JsonValueKind.Array)
        {
            return false;
        }

        foreach (var record in records.EnumerateArray())
        {
            if (record.ValueKind is JsonValueKind.Object
                && record.TryGetProperty("Id", out var id)
                && record.TryGetProperty("Description", out var description)
                && string.Equals(id.GetString(), mutation.AccountId, StringComparison.Ordinal)
                && string.Equals(description.GetString(), mutation.Description, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string RequiredFingerprint(string? fingerprint, string tool)
        => !string.IsNullOrWhiteSpace(fingerprint)
            ? fingerprint
            : throw new InvalidOperationException(
                $"The durable invoking fence carries no admitted schema fingerprint for '{tool}'.");

    private static SelectedTool SelectUpdateTool(IList<McpClientTool> tools) =>
        SelectTool(
            tools,
            UpdateAccountName,
            tool => HasExactObjectSchema(
                    tool.ProtocolTool.InputSchema,
                    ("sobject-name", "string"),
                    ("id", "string"),
                    ("body", "object"))
                && HasExactObjectSchema(
                    tool.ProtocolTool.OutputSchema,
                    ("success", "boolean"))
                && HasExactAnnotations(
                    tool,
                    readOnly: false,
                    destructive: true,
                    idempotent: false,
                    openWorld: false));

    private static SelectedTool SelectQueryTool(IList<McpClientTool> tools) =>
        SelectTool(
            tools,
            QueryAccountName,
            tool => HasExactObjectSchema(
                    tool.ProtocolTool.InputSchema,
                    ("query", "string"))
                && HasQueryOutputSchema(tool.ProtocolTool.OutputSchema)
                && HasExactAnnotations(
                    tool,
                    readOnly: true,
                    destructive: false,
                    idempotent: true,
                    openWorld: false));

    private static SelectedTool SelectTool(IList<McpClientTool> tools, string name, Func<McpClientTool, bool> compatible)
    {
        var matches = tools
            .Where(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal))
            .ToArray();

        if (matches.Length != 1)
        {
            throw Incompatible(name);
        }

        var tool = matches[0];
        if (!compatible(tool))
        {
            throw Incompatible(tool.Name);
        }

        var annotations = tool.ProtocolTool.Annotations;
        return new SelectedTool(
            tool,
            McpToolFingerprint.Create(
                tool.ProtocolTool.InputSchema,
                tool.ProtocolTool.OutputSchema,
                annotations?.ReadOnlyHint,
                annotations?.DestructiveHint,
                annotations?.IdempotentHint,
                annotations?.OpenWorldHint));
    }

    private static bool HasExactObjectSchema(JsonElement? schema, params (string Name, string Type)[] expected)
    {
        if (schema is not { ValueKind: JsonValueKind.Object } value
            || !value.TryGetProperty("type", out var schemaType)
            || !string.Equals(schemaType.GetString(), "object", StringComparison.Ordinal)
            || !value.TryGetProperty("properties", out var properties)
            || properties.ValueKind is not JsonValueKind.Object
            || !value.TryGetProperty("required", out var required)
            || required.ValueKind is not JsonValueKind.Array)
        {
            return false;
        }

        var expectedNames = expected
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var propertyNames = properties
            .EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var requiredNames = required
            .EnumerateArray()
            .Select(property => property.GetString())
            .Order(StringComparer.Ordinal)
            .ToArray();

        return propertyNames.SequenceEqual(expectedNames, StringComparer.Ordinal)
            && requiredNames.SequenceEqual(expectedNames, StringComparer.Ordinal)
            && expected.All(property =>
                properties.TryGetProperty(property.Name, out var definition)
                && definition.ValueKind is JsonValueKind.Object
                && definition.TryGetProperty("type", out var type)
                && string.Equals(type.GetString(), property.Type, StringComparison.Ordinal));
    }

    private static bool HasQueryOutputSchema(JsonElement? schema) =>
        HasExactObjectSchema(schema, ("records", "array"))
        && schema is { } value
        && value.GetProperty("properties")
            .GetProperty("records")
            .TryGetProperty("items", out var items)
        && items.ValueKind is JsonValueKind.Object
        && items.TryGetProperty("type", out var itemType)
        && string.Equals(itemType.GetString(), "object", StringComparison.Ordinal);

    private static bool HasExactAnnotations(
        McpClientTool tool,
        bool readOnly,
        bool destructive,
        bool idempotent,
        bool openWorld)
    {
        var annotations = tool.ProtocolTool.Annotations;
        return annotations?.ReadOnlyHint == readOnly
            && annotations.DestructiveHint == destructive
            && annotations.IdempotentHint == idempotent
            && annotations.OpenWorldHint == openWorld;
    }

    private static InvalidOperationException Incompatible(string tool) =>
        new($"{Server.DisplayName} MCP tool '{tool}' is incompatible with its admitted contract.");

    private sealed record SelectedTool(McpClientTool Tool, string Fingerprint);
}
