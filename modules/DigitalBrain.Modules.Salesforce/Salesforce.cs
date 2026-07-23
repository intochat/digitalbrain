using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Integrations.Mcp;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Salesforce;

internal sealed class Salesforce : Neuron, ISalesforce
{
    private const string MutationsName = "salesforce.mutations";
    private const string QueryAccountName = "soqlQuery";
    private const string TokensName = "salesforce.oauth";
    private const string UpdateAccountName = "updateSobjectRecord";
    private static readonly McpServerDefinition Server = new(
        "salesforce",
        "DigitalBrain Salesforce",
        new Uri("https://api.salesforce.com/platform/mcp/v1/platform/sobject-mutations"),
        "DigitalBrain:Salesforce",
        ["mcp_api", "refresh_token"],
        requiresClientSecret: false);
    private readonly string _durableIdentity;
    private readonly IDurableDictionary<Guid, byte[]> _mutations;
    private readonly McpRuntime _runtime;
    private readonly TimeSpan _reconciliationTimeout;
    private readonly Serializer<MutationData> _states;
    private readonly IDurableValue<byte[]> _tokenState;

    public Salesforce(
        McpRuntime runtime,
        SalesforceRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.ReconciliationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.ReconciliationTimeout,
                "Salesforce reconciliation timeout must be positive.");
        }

        _runtime = runtime;
        _reconciliationTimeout = options.ReconciliationTimeout;
        _mutations = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<Guid, byte[]>>(
            MutationsName);
        _states = ServiceProvider.GetRequiredService<Serializer<MutationData>>();
        _tokenState = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(TokensName);
        _durableIdentity = Id.ToString();
    }

    public async Task<SalesforceAccountDescriptionMutation> ProposeAccountDescriptionAsync(
        CommandId commandId,
        NeuronId requester,
        string accountId,
        string description,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(commandId, accountId, description);
        ValidateCapabilityCaller(requester);

        var fingerprint = Fingerprint(accountId, description);

        if (TryLoad(commandId, out var existing))
        {
            EnsureSame(existing, fingerprint);
            return Receipt(existing);
        }

        var proposed = new MutationData(
            commandId,
            requester,
            accountId,
            description,
            fingerprint,
            UpdateSchemaFingerprint: null,
            QuerySchemaFingerprint: null,
            Approval: null,
            ApprovalEvidence: null,
            MutationStatus.AwaitingApproval);
        await SaveAsync(proposed, add: true);

        return Receipt(proposed);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Any failure after durable Invoking makes the external mutation outcome uncertain and must not escape into an automatic retry path.")]
    public async Task<SalesforceAccountDescriptionMutation> ApproveAccountDescriptionAsync(
        SalesforceMutationApproval approval,
        SynapseDelivery approvalEvidence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(approvalEvidence);
        Validate(approval, approvalEvidence);

        var mutation = TryLoad(approval.CommandId, out var loaded)
            ? loaded
            : throw new InvalidOperationException(
                $"Salesforce mutation '{approval.CommandId}' has not been proposed.");
        ValidateCapabilityCaller(mutation.Requester);
        EnsureSame(mutation, approval.Fingerprint);

        if (mutation.Status is MutationStatus.Completed or MutationStatus.OutcomeUncertain)
        {
            ValidateApprovalEvidence(mutation, approval, approvalEvidence);
            EnsureSameApproval(mutation, approval);
            return Receipt(mutation);
        }

        if (mutation.Status is MutationStatus.Invoking)
        {
            ValidateApprovalEvidence(mutation, approval, approvalEvidence);
            EnsureSameApproval(mutation, approval);
            mutation = await ReconcileBoundedAsync(mutation);
            await SaveAsync(mutation);
            return Receipt(mutation);
        }

        if (mutation.Status is not MutationStatus.AwaitingApproval)
        {
            throw new InvalidOperationException(
                $"Salesforce mutation '{approval.CommandId}' cannot be approved from {mutation.Status}.");
        }

        ValidateApprovalEvidence(mutation, approval, approvalEvidence);
        cancellationToken.ThrowIfCancellationRequested();

        var admitted = await _runtime.RunAsync(
            Server,
            _tokenState,
            () => WriteStateAsync(),
            _durableIdentity,
            async (client, callbackCancellation) =>
            {
                var tools = await client.ListToolsAsync(cancellationToken: callbackCancellation);
                return (
                    Update: SelectUpdateTool(tools).Fingerprint,
                    Query: SelectQueryTool(tools).Fingerprint);
            },
            cancellationToken);
        mutation = mutation with
        {
            Approval = approval,
            ApprovalEvidence = approvalEvidence.SynapseId,
            UpdateSchemaFingerprint = admitted.Update,
            QuerySchemaFingerprint = admitted.Query,
            Status = MutationStatus.Invoking,
        };
        await SaveAsync(mutation);

        try
        {
            mutation = await _runtime.RunAsync(
                Server,
                _tokenState,
                () => WriteStateAsync(),
                _durableIdentity,
                async (client, callbackCancellation) =>
                {
                    var tools = await client.ListToolsAsync(cancellationToken: callbackCancellation);
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
                        Arguments(mutation),
                        cancellationToken: callbackCancellation);
                    var content = McpRuntime.RequireStructuredContent(
                        result,
                        Server,
                        UpdateAccountName);

                    return mutation with
                    {
                        Status = IsSuccessfulUpdate(content)
                            ? MutationStatus.Completed
                            : MutationStatus.Invoking,
                    };
                },
                cancellationToken);
        }
        catch (Exception)
        {
            mutation = await ReconcileBoundedAsync(mutation);
        }

        if (mutation.Status is MutationStatus.Invoking)
        {
            mutation = await ReconcileBoundedAsync(mutation);
        }

        await SaveAsync(mutation);
        return Receipt(mutation);
    }

    private static Dictionary<string, object?> Arguments(MutationData mutation)
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
        using var reconciliation = new CancellationTokenSource(_reconciliationTimeout);
        return await ReconcileAsync(mutation, reconciliation.Token);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Reconciliation is best effort; inability to prove the provider state must durably become OutcomeUncertain.")]
    private async Task<MutationData> ReconcileAsync(
        MutationData mutation,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await _runtime.RunAsync(
                Server,
                _tokenState,
                () => WriteStateAsync(),
                _durableIdentity,
                async (client, callbackCancellation) =>
                {
                    var tools = await client.ListToolsAsync(cancellationToken: callbackCancellation);
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
                        cancellationToken: callbackCancellation);
                    return McpRuntime.RequireStructuredContent(result, Server, QueryAccountName);
                },
                cancellationToken);

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

    private static SelectedTool SelectTool(
        IList<McpClientTool> tools,
        string name,
        Func<McpClientTool, bool> compatible)
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

    private static bool HasExactObjectSchema(
        JsonElement? schema,
        params (string Name, string Type)[] expected)
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

    private async Task SaveAsync(MutationData mutation, bool add = false)
    {
        var key = mutation.CommandId.Value;
        var serialized = _states.SerializeToArray(mutation);
        var existed = _mutations.TryGetValue(key, out var previous);

        try
        {
            if (add)
            {
                _mutations.Add(key, serialized);
            }
            else
            {
                _mutations[key] = serialized;
            }

            await WriteStateAsync();
        }
        catch
        {
            if (existed)
            {
                _mutations[key] = previous!;
            }
            else
            {
                _mutations.Remove(key);
            }

            throw;
        }
    }

    private bool TryLoad(CommandId commandId, out MutationData mutation)
    {
        if (_mutations.TryGetValue(commandId.Value, out var serialized))
        {
            mutation = _states.Deserialize(serialized);
            return true;
        }

        mutation = null!;
        return false;
    }

    private static void EnsureSame(MutationData mutation, string fingerprint)
    {
        if (!string.Equals(mutation.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"CommandId '{mutation.CommandId}' is already bound to a different Salesforce mutation fingerprint.");
        }
    }

    private static void EnsureSameApproval(
        MutationData mutation,
        SalesforceMutationApproval approval)
    {
        if (mutation.Approval != approval)
        {
            throw new NeuronAuthorizationException(
                $"Salesforce mutation '{mutation.CommandId}' is bound to different approval evidence.");
        }
    }

    private static void ValidateApprovalEvidence(
        MutationData mutation,
        SalesforceMutationApproval approval,
        SynapseDelivery evidence)
    {
        if (evidence.Caller != approval.Approver
            || evidence.Synapse is not SalesforceMutationApproval recorded
            || recorded != approval
            || (mutation.Approval is not null
                && mutation.ApprovalEvidence != evidence.SynapseId))
        {
            throw new NeuronAuthorizationException(
                $"Salesforce mutation '{mutation.CommandId}' has no exact durable human approval evidence.");
        }
    }

    private static SalesforceAccountDescriptionMutation Receipt(MutationData mutation)
        => new(
            mutation.CommandId,
            mutation.AccountId,
            mutation.Description,
            mutation.Fingerprint,
            mutation.Status switch
            {
                MutationStatus.Completed => SalesforceMutationState.Completed,
                MutationStatus.OutcomeUncertain => SalesforceMutationState.OutcomeUncertain,
                _ => SalesforceMutationState.AwaitingApproval,
            });

    private static string Fingerprint(string accountId, string description)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            tool = UpdateAccountName,
            sobject = "Account",
            id = accountId,
            body = new { Description = description },
        });

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static void Validate(CommandId commandId, string accountId, string description)
    {
        if (commandId == default)
        {
            throw new ArgumentException("A mutation command identity is required.", nameof(commandId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ValidateAccountId(accountId);
    }

    private static void ValidateAccountId(string accountId)
    {
        if (accountId.Length is not (15 or 18)
            || accountId.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException(
                "A Salesforce Account ID must be a 15- or 18-character alphanumeric value.",
                nameof(accountId));
        }
    }

    private void Validate(
        SalesforceMutationApproval approval,
        SynapseDelivery approvalEvidence)
    {
        if (approval.CommandId == default)
        {
            throw new ArgumentException(
                "A mutation command identity is required.",
                nameof(approval));
        }

        if (approval.ApprovalId == Guid.Empty || approvalEvidence.SynapseId == default)
        {
            throw new ArgumentException(
                "Durable approval identity and evidence are required.",
                nameof(approval));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(approval.Fingerprint);

        if (approval.Approver.Owner != Id.Owner
            || approval.Approver.Type != ISessionNeuron.GrainTypeName
            || approval.ApprovedAt == default)
        {
            throw new NeuronAuthorizationException(
                "Salesforce mutation approval must be issued by this owner's human session.");
        }
    }

    [GenerateSerializer]
    internal sealed record MutationData(
        [property: Id(0)] CommandId CommandId,
        [property: Id(1)] NeuronId Requester,
        [property: Id(2)] string AccountId,
        [property: Id(3)] string Description,
        [property: Id(4)] string Fingerprint,
        [property: Id(5)] string? UpdateSchemaFingerprint,
        [property: Id(6)] string? QuerySchemaFingerprint,
        [property: Id(7)] SalesforceMutationApproval? Approval,
        [property: Id(8)] SynapseId? ApprovalEvidence,
        [property: Id(9)] MutationStatus Status);

    private sealed record SelectedTool(McpClientTool Tool, string Fingerprint);

    internal enum MutationStatus
    {
        AwaitingApproval,
        Invoking,
        Completed,
        OutcomeUncertain,
    }
}

internal sealed record SalesforceRuntimeOptions(TimeSpan ReconciliationTimeout)
{
    internal static SalesforceRuntimeOptions Default { get; } =
        new(TimeSpan.FromSeconds(30));
}
