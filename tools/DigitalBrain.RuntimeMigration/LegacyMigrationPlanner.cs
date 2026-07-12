using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;

namespace DigitalBrain.RuntimeMigration;

public sealed class LegacyMigrationPlanner(Uri? expectedOAuthOrigin = null)
{
    private const string OperationDomain = "digitalbrain.v2.operations";
    private const string ConversationDomain = "digitalbrain.v2.ino-effects";
    private const string ConversationKind = "conversation.snapshot";
    private const int ConversationJournalVersion = 4;
    private readonly Uri? _expectedOAuthOrigin = ValidateOrigin(expectedOAuthOrigin);

    public RuntimeMigrationPlan Plan(VerifiedJournal operationJournal, VerifiedJournal conversationJournal)
    {
        if (!string.Equals(operationJournal.Domain, OperationDomain, StringComparison.Ordinal) ||
            !string.Equals(conversationJournal.Domain, ConversationDomain, StringComparison.Ordinal))
            throw new MigrationGapException("journal-domain-mismatch");

        var operations = ReduceOperations(operationJournal);
        var snapshots = ReduceConversations(conversationJournal);
        var sourceDigest = MigrationHash.Sha256(string.Join('\n',
            "digitalbrain-runtime-migration-source-v1",
            operationJournal.Domain,
            operationJournal.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            operationJournal.HeadDigest.ToLowerInvariant(),
            conversationJournal.Domain,
            conversationJournal.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            conversationJournal.HeadDigest.ToLowerInvariant()));
        var migrationId = "legacy-v2-" + sourceDigest;
        var conversations = snapshots
            .Select(pair => MapConversation(pair.Key, pair.Value, operations, migrationId))
            .OrderBy(static conversation => conversation.GrainKey, StringComparer.Ordinal)
            .ToArray();
        var expectedDigest = MigrationHash.Sha256(string.Join('\n',
            conversations.Select(static conversation => conversation.GrainKey + ":" + conversation.ExpectedDigest)
                .Prepend("digitalbrain-runtime-migration-destination-v1")));
        return new RuntimeMigrationPlan(
            1,
            sourceDigest,
            migrationId,
            MigrationHash.Sha256(migrationId),
            expectedDigest,
            conversations);
    }

    private static IReadOnlyDictionary<ScopedCommand, LegacyPersistedOperation> ReduceOperations(
        VerifiedJournal journal)
    {
        var idempotencyReceipts = new Dictionary<ScopedIdempotency, OperationReceipt>();
        var operationIdempotency = new Dictionary<string, string>(StringComparer.Ordinal);
        var operationCommands = new Dictionary<string, OperationReceipt>(StringComparer.Ordinal);
        var latestByOperation = new Dictionary<string, LegacyPersistedOperation>(StringComparer.Ordinal);
        foreach (var record in journal.Records)
        {
            LegacyPersistedOperation item;
            try
            {
                item = JsonSerializer.Deserialize<LegacyPersistedOperation>(record.Payload)
                       ?? throw new JsonException();
            }
            catch (JsonException)
            {
                throw new MigrationGapException("operation-record-invalid");
            }
            ValidateOperationRecord(item, record);
            var command = item.Command!;
            var inputFingerprint = CanonicalHash(command.Payload);
            var receipt = new OperationReceipt(
                item.Operation.OperationId,
                command.Type,
                command.Version,
                inputFingerprint,
                command.Context.TenantId,
                command.Context.WorkspaceId,
                command.Context.Principal,
                command.CommandId);
            var idempotency = new ScopedIdempotency(
                command.Context.TenantId,
                command.Context.WorkspaceId,
                command.Context.Principal,
                item.Idempotency);
            if ((idempotencyReceipts.TryGetValue(idempotency, out var priorReceipt) && priorReceipt != receipt) ||
                (operationIdempotency.TryGetValue(item.Operation.OperationId, out var priorIdempotency) &&
                 !string.Equals(priorIdempotency, item.Idempotency, StringComparison.Ordinal)) ||
                (operationCommands.TryGetValue(item.Operation.OperationId, out var priorCommand) &&
                 priorCommand != receipt))
                throw new MigrationGapException("operation-idempotency-conflict");
            idempotencyReceipts[idempotency] = receipt;
            operationIdempotency[item.Operation.OperationId] = item.Idempotency;
            operationCommands[item.Operation.OperationId] = receipt;
            latestByOperation[item.Operation.OperationId] = item;
        }

        var selected = new Dictionary<ScopedCommand, LegacyPersistedOperation>();
        foreach (var item in latestByOperation.Values.Where(static item =>
                     string.Equals(item.Command!.Type, "ino.interact", StringComparison.Ordinal)))
        {
            var command = item.Command!;
            var key = new ScopedCommand(
                command.Context.TenantId,
                command.Context.WorkspaceId,
                command.Context.Principal,
                command.CommandId);
            if (!selected.TryAdd(key, item))
                throw new MigrationGapException("conversation-command-ambiguous");
        }
        return selected;
    }

    private static Dictionary<LegacyConversationScope, LegacyPersistedConversation> ReduceConversations(
        VerifiedJournal journal)
    {
        var result = new Dictionary<LegacyConversationScope, LegacyPersistedConversation>();
        var sourceVersions = new Dictionary<LegacyConversationScope, int>();
        foreach (var record in journal.Records)
        {
            if (!record.IsLegacy && !string.Equals(record.Kind, ConversationKind, StringComparison.Ordinal))
                throw new MigrationGapException("conversation-record-kind-invalid");
            LegacyPersistedConversation item;
            try
            {
                item = JsonSerializer.Deserialize<LegacyPersistedConversation>(record.Payload)
                       ?? throw new JsonException();
            }
            catch (JsonException)
            {
                throw new MigrationGapException("conversation-record-json-invalid");
            }
            var sourceVersion = item.Version;
            item = NormalizeConversationRecord(item);
            ValidateConversationRecord(item);
            var scope = new LegacyConversationScope(item.Tenant, item.Workspace, item.Principal);
            if (!result.TryGetValue(scope, out var prior) || item.Snapshot.Revision > prior.Snapshot.Revision)
            {
                result[scope] = item;
                sourceVersions[scope] = sourceVersion;
                continue;
            }
            if (item.Snapshot.Revision != prior.Snapshot.Revision) continue;

            var priorSourceVersion = sourceVersions[scope];
            if (sourceVersion == ConversationJournalVersion && priorSourceVersion != ConversationJournalVersion)
            {
                result[scope] = item;
                sourceVersions[scope] = sourceVersion;
                continue;
            }
            if (sourceVersion != ConversationJournalVersion || priorSourceVersion != ConversationJournalVersion)
                continue;
            if (!FixedTimeEquals(
                    MigrationHash.Sha256(JsonSerializer.Serialize(item)),
                    MigrationHash.Sha256(JsonSerializer.Serialize(prior))))
                throw new MigrationGapException("conversation-revision-conflict");
        }
        return result;
    }

    private static LegacyPersistedConversation NormalizeConversationRecord(LegacyPersistedConversation item)
    {
        if (item.Version == ConversationJournalVersion || item.Version is not (2 or 3) ||
            item.Snapshot is null || item.Snapshot.Operations is null)
            return item;
        if (item.Snapshot.Operations.Any(static operation => operation is null))
            return item with { Version = ConversationJournalVersion };

        var scrubbedTerminalMaterial = false;
        var operations = item.Snapshot.Operations.Select(operation =>
        {
            if (InoConversationStates.IsActive(operation.State) ||
                operation.Action is null && operation.Authorization is null)
                return operation;
            scrubbedTerminalMaterial = true;
            return operation with { Action = null, Authorization = null };
        }).ToArray();
        var snapshot = item.Snapshot with { Operations = operations };
        if (scrubbedTerminalMaterial)
        {
            if (snapshot.Revision == int.MaxValue)
                throw new MigrationGapException("conversation-revision-unrepresentable");
            snapshot = snapshot with { Revision = snapshot.Revision + 1 };
        }
        return item with { Version = ConversationJournalVersion, Snapshot = snapshot };
    }

    private ConversationImportPlan MapConversation(
        LegacyConversationScope scope,
        LegacyPersistedConversation persisted,
        IReadOnlyDictionary<ScopedCommand, LegacyPersistedOperation> operations,
        string migrationId)
    {
        var snapshot = persisted.Snapshot;
        var context = MigrationHash.ValidationContext(scope);
        var expectedConversationId = InoConversationIdentity.From(context);
        if (!string.Equals(snapshot.ConversationId, expectedConversationId, StringComparison.Ordinal))
            throw new MigrationGapException("conversation-identity-mismatch");
        if (snapshot.Turns.Count > ConversationTransitions.MaximumInlineTurns ||
            snapshot.Operations.Count > ConversationTransitions.MaximumTerminalOperations)
            throw new MigrationGapException("conversation-retention-unrepresentable");

        var plannedOperations = new Dictionary<string, PlannedOperation>(StringComparer.Ordinal);
        foreach (var legacyOperation in snapshot.Operations)
        {
            if (legacyOperation is null) throw new MigrationGapException("conversation-operation-invalid");
            ValidateSnapshotOperation(legacyOperation);
            var key = new ScopedCommand(scope.Tenant, scope.Workspace, scope.Principal, legacyOperation.CommandId);
            if (!operations.TryGetValue(key, out var operationRecord))
                throw new MigrationGapException("operation-record-missing");
            var command = operationRecord.Command!;
            if (command.Version != 2) throw new MigrationGapException("command-version-unrepresentable");
            var idempotency = command.Context.IdempotencyKey ?? command.CommandId;
            if (!string.Equals(operationRecord.Idempotency, idempotency, StringComparison.Ordinal) ||
                idempotency.Length > 256 || idempotency.Any(char.IsControl))
                throw new MigrationGapException("idempotency-not-representable");
            var prompt = ReadPrompt(command.Payload);
            if (!string.Equals(prompt, legacyOperation.Prompt, StringComparison.Ordinal))
                throw new MigrationGapException("operation-prompt-mismatch");
            var destination = MapOperation(scope, legacyOperation, operationRecord);
            var planned = new PlannedOperation(
                legacyOperation.CommandId,
                MigrationHash.PromptHash(legacyOperation.Prompt),
                legacyOperation.Prompt,
                destination);
            if (!plannedOperations.TryAdd(legacyOperation.CommandId, planned))
                throw new MigrationGapException("conversation-operation-duplicate");
        }

        var seenUsers = new HashSet<string>(StringComparer.Ordinal);
        var seenAssistants = new HashSet<string>(StringComparer.Ordinal);
        var turns = new List<PlannedTurn>(snapshot.Turns.Count);
        foreach (var legacyTurn in snapshot.Turns)
        {
            if (legacyTurn is null) throw new MigrationGapException("conversation-turn-invalid");
            if (!plannedOperations.TryGetValue(legacyTurn.CommandId, out var operation))
                throw new MigrationGapException("conversation-turn-orphaned");
            if (legacyTurn.Role is not ("user" or "assistant") ||
                string.IsNullOrWhiteSpace(legacyTurn.Text) || legacyTurn.Text.Length > 16_000)
                throw new MigrationGapException("conversation-turn-invalid");
            var sourceOperation = snapshot.Operations.Single(candidate =>
                string.Equals(candidate.CommandId, legacyTurn.CommandId, StringComparison.Ordinal));
            if (!string.Equals(legacyTurn.State, sourceOperation.State, StringComparison.Ordinal))
                throw new MigrationGapException("conversation-turn-state-mismatch");
            if (legacyTurn.Role == "user")
            {
                if (!seenUsers.Add(legacyTurn.CommandId) ||
                    !string.Equals(legacyTurn.Text, sourceOperation.Prompt, StringComparison.Ordinal))
                    throw new MigrationGapException("conversation-user-turn-invalid");
            }
            else if (!seenUsers.Contains(legacyTurn.CommandId) || !seenAssistants.Add(legacyTurn.CommandId))
            {
                throw new MigrationGapException("conversation-assistant-turn-invalid");
            }
            turns.Add(new PlannedTurn(
                operation.Destination.CommandId,
                operation.Destination.OperationId,
                legacyTurn.Role,
                legacyTurn.Text,
                operation.Destination.UpdatedAt));
        }
        if (plannedOperations.Keys.Any(commandId => !seenUsers.Contains(commandId)))
            throw new MigrationGapException("conversation-user-turn-missing");
        foreach (var operation in snapshot.Operations)
        {
            var expectsAssistant = operation.State is
                InoConversationStates.AwaitingAuthorization or InoConversationStates.Succeeded;
            if (seenAssistants.Contains(operation.CommandId) != expectsAssistant)
                throw new MigrationGapException("conversation-assistant-turn-mismatch");
        }

        var identity = new ConversationIdentity(scope.Tenant, scope.Workspace, scope.Principal, snapshot.ConversationId);
        var orderedOperations = snapshot.Operations.Select(operation => plannedOperations[operation.CommandId]).ToArray();
        var expectedDigest = MigrationHash.ConversationDigest(identity, turns, orderedOperations);
        return new ConversationImportPlan(
            MigrationHash.GrainKey(scope, snapshot.ConversationId),
            identity,
            turns,
            orderedOperations,
            migrationId,
            expectedDigest);
    }

    private ConversationOperation MapOperation(
        LegacyConversationScope scope,
        InoConversationOperation snapshot,
        LegacyPersistedOperation record)
    {
        if (record.AuthorizationResolution is not null)
            throw new MigrationGapException("authorization-resolution-unrepresentable");
        var (status, terminalPolicy, expectedSnapshotState) = record.Operation.State switch
        {
            WorkflowState.ApplyQueued => (
                ConversationOperationStatus.Pending,
                ConversationTerminalPolicy.VerifyBeforeRetry,
                InoConversationStates.Queued),
            WorkflowState.AwaitingExternalAuthorization => (
                ConversationOperationStatus.AwaitingAuthorization,
                ConversationTerminalPolicy.VerifyBeforeRetry,
                InoConversationStates.AwaitingAuthorization),
            WorkflowState.Succeeded => (
                ConversationOperationStatus.Succeeded,
                ConversationTerminalPolicy.NeverRetry,
                InoConversationStates.Succeeded),
            WorkflowState.Failed => (
                ConversationOperationStatus.Failed,
                ConversationTerminalPolicy.NeverRetry,
                InoConversationStates.Failed),
            WorkflowState.OutcomeUnknown => (
                ConversationOperationStatus.OutcomeUnknown,
                ConversationTerminalPolicy.ManualIntervention,
                InoConversationStates.Failed),
            WorkflowState.Cancelled => (
                ConversationOperationStatus.Cancelled,
                ConversationTerminalPolicy.NeverRetry,
                InoConversationStates.Failed),
            _ => throw new MigrationGapException("workflow-state-unrepresentable")
        };
        if (!string.Equals(snapshot.State, expectedSnapshotState, StringComparison.Ordinal) ||
            !string.Equals(snapshot.SafeReason, record.Operation.SafeReason, StringComparison.Ordinal))
            throw new MigrationGapException("operation-state-mismatch");
        if ((status == ConversationOperationStatus.AwaitingAuthorization && !snapshot.Retryable) ||
            (status != ConversationOperationStatus.AwaitingAuthorization && snapshot.Retryable))
            throw new MigrationGapException("retry-state-unrepresentable");

        SuspendedInvocation? suspended = null;
        DateTimeOffset? nextAttemptAt = null;
        var updatedAt = record.Operation.UpdatedAt;
        if (status == ConversationOperationStatus.AwaitingAuthorization)
        {
            if (record.Authorization is null || snapshot.Authorization is null || snapshot.Action is null ||
                !record.Authorization.Matches(snapshot.Authorization))
                throw new MigrationGapException("authorization-continuation-mismatch");
            if (!TryExtractFlowReference(snapshot.Action, record.Authorization.Provider, out var flowReference))
                throw new MigrationGapException("authorization-flow-unrepresentable");
            if (record.Authorization.ExpiresAt <= DateTimeOffset.MinValue.AddTicks(1))
                throw new MigrationGapException("authorization-expiry-unrepresentable");
            if (updatedAt >= record.Authorization.ExpiresAt)
                updatedAt = record.Authorization.ExpiresAt.AddTicks(-1);
            var inputUtf8 = Encoding.UTF8.GetBytes(record.Authorization.Invocation.Input.GetRawText());
            suspended = new SuspendedInvocation(
                record.Authorization.Provider,
                record.Authorization.Invocation.ToolId,
                inputUtf8,
                record.Authorization.AttemptId,
                record.Authorization.ExpiresAt,
                flowReference);
            nextAttemptAt = record.Authorization.ExpiresAt;
        }
        else if (record.Authorization is not null || snapshot.Authorization is not null)
        {
            throw new MigrationGapException("authorization-state-mismatch");
        }

        return new ConversationOperation(
            MigrationHash.OperationId(scope, record.Idempotency),
            record.Idempotency,
            status,
            0,
            nextAttemptAt,
            null,
            null,
            terminalPolicy,
            record.Operation.SafeReason,
            suspended,
            updatedAt);
    }

    private bool TryExtractFlowReference(ToolAction action, string provider, out string flowReference)
    {
        flowReference = string.Empty;
        if (!string.Equals(action.Kind, "openUrl", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(action.Target) || action.Target.Length > 4096 ||
            !Uri.TryCreate(action.Target, UriKind.RelativeOrAbsolute, out var uri)) return false;

        string pathAndQuery;
        if (uri.IsAbsoluteUri)
        {
            if (!string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo) ||
                _expectedOAuthOrigin is null ||
                !SameOrigin(uri, _expectedOAuthOrigin)) return false;
            pathAndQuery = uri.PathAndQuery;
        }
        else
        {
            if (!action.Target.StartsWith("/", StringComparison.Ordinal) ||
                action.Target.StartsWith("//", StringComparison.Ordinal) ||
                action.Target.Contains('#')) return false;
            pathAndQuery = action.Target;
        }
        var question = pathAndQuery.IndexOf('?');
        var path = question < 0 ? pathAndQuery : pathAndQuery[..question];
        if (!string.Equals(path, $"/oauth/start/{provider}", StringComparison.Ordinal) || question < 0)
            return false;
        var pairs = pathAndQuery[(question + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries);
        if (pairs.Length != 1) return false;
        var separator = pairs[0].IndexOf('=');
        if (separator <= 0 || pairs[0][..separator] is not ("f" or "t")) return false;
        var candidate = pairs[0][(separator + 1)..];
        if (!IsOpaqueFlowReference(candidate)) return false;
        flowReference = candidate;
        return true;
    }

    private static void ValidateOperationRecord(LegacyPersistedOperation item, VerifiedJournalRecord record)
    {
        var command = item.Command;
        if (string.IsNullOrWhiteSpace(item.Idempotency) || item.Idempotency.Length > 1024 ||
            string.IsNullOrWhiteSpace(item.Tenant) || item.Tenant.Length > 256 ||
            string.IsNullOrWhiteSpace(item.Workspace) || item.Workspace.Length > 256 ||
            item.Operation is null || string.IsNullOrWhiteSpace(item.Operation.OperationId) ||
            item.Operation.OperationId.Length > 256 || !Enum.IsDefined(item.Operation.State) ||
            item.Operation.UpdatedAt == default || command is null || command.Context is null ||
            command.Context.TenantId.Value != item.Tenant || command.Context.WorkspaceId.Value != item.Workspace ||
            string.IsNullOrWhiteSpace(command.Context.Principal.Value) || command.Context.Principal.Value.Length > 256 ||
            !Enum.IsDefined(command.Context.Principal.Kind) || !Enum.IsDefined(command.Context.Assurance) ||
            string.IsNullOrWhiteSpace(command.Context.SessionId) || command.Context.SessionId.Length > 256 ||
            command.Context.Grants is null || string.IsNullOrWhiteSpace(command.Type) || command.Type.Length > 256 ||
            command.Version <= 0 || string.IsNullOrWhiteSpace(command.CommandId) || command.CommandId.Length > 1024 ||
            command.Payload.ValueKind == JsonValueKind.Undefined ||
            item.Operation.State == WorkflowState.AwaitingExternalAuthorization && item.Authorization is null ||
            item.Authorization is not null &&
            (!item.Authorization.IsValid() || item.Operation.State is not (
                WorkflowState.AwaitingExternalAuthorization or WorkflowState.ApplyQueued or WorkflowState.Applying)) ||
            item.AuthorizationResolution is not null &&
            (item.Authorization is null ||
             item.AuthorizationResolution.State is not (
                 ExternalAuthorizationResolutionState.Ready or ExternalAuthorizationResolutionState.Failed) ||
             item.AuthorizationResolution.SafeReason is { Length: > 256 } ||
             item.Operation.State is not (WorkflowState.ApplyQueued or WorkflowState.Applying)))
            throw new MigrationGapException("operation-record-invalid");
        if (!record.IsLegacy && !string.Equals(record.Kind, "operation." + item.Operation.State, StringComparison.Ordinal))
            throw new MigrationGapException("operation-record-kind-invalid");
    }

    private static void ValidateConversationRecord(LegacyPersistedConversation item)
    {
        if (item.Version != ConversationJournalVersion)
            throw new MigrationGapException("conversation-version-unrepresentable");
        if (string.IsNullOrWhiteSpace(item.Tenant.Value) || item.Tenant.Value.Length > 256 ||
            string.IsNullOrWhiteSpace(item.Workspace.Value) || item.Workspace.Value.Length > 256 ||
            string.IsNullOrWhiteSpace(item.Principal.Value) || item.Principal.Value.Length > 256 ||
            !Enum.IsDefined(item.Principal.Kind))
            throw new MigrationGapException("conversation-scope-invalid");
        if (item.Snapshot is null)
            throw new MigrationGapException("conversation-snapshot-missing");
        if (string.IsNullOrWhiteSpace(item.Snapshot.ConversationId) || item.Snapshot.ConversationId.Length > 256 ||
            item.Snapshot.Revision < 0 || item.Snapshot.Turns is null || item.Snapshot.Operations is null)
            throw new MigrationGapException("conversation-snapshot-invalid");
    }

    private static void ValidateSnapshotOperation(InoConversationOperation operation)
    {
        if (string.IsNullOrWhiteSpace(operation.CommandId) || operation.CommandId.Length > 256 ||
            operation.CommandId.Any(char.IsControl) || string.IsNullOrWhiteSpace(operation.Prompt) ||
            operation.Prompt.Length > 4096 || !string.Equals(operation.Prompt, operation.Prompt.Trim(), StringComparison.Ordinal) ||
            operation.SafeReason is { Length: > 256 } || operation.UpdatedAt == default ||
            operation.State is not (InoConversationStates.Queued or InoConversationStates.AwaitingAuthorization or
                InoConversationStates.Succeeded or InoConversationStates.Failed))
            throw new MigrationGapException("conversation-operation-invalid");
    }

    private static string ReadPrompt(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) throw new MigrationGapException("command-payload-unrepresentable");
        var properties = payload.EnumerateObject().ToArray();
        if (properties.Length != 1 || !string.Equals(properties[0].Name, "prompt", StringComparison.Ordinal) ||
            properties[0].Value.ValueKind != JsonValueKind.String)
            throw new MigrationGapException("command-payload-unrepresentable");
        var prompt = properties[0].Value.GetString()?.Trim() ?? string.Empty;
        if (prompt.Length is not (> 0 and <= 4096))
            throw new MigrationGapException("command-payload-unrepresentable");
        return prompt;
    }

    private static string CanonicalHash(JsonElement input)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, input);
            writer.Flush();
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(value.GetRawText(), skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new MigrationGapException("operation-record-invalid");
        }
    }

    private static Uri? ValidateOrigin(Uri? origin)
    {
        if (origin is null) return null;
        if (!origin.IsAbsoluteUri || origin.Scheme is not ("http" or "https") ||
            !string.Equals(origin.AbsolutePath, "/", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment) ||
            !string.IsNullOrEmpty(origin.UserInfo))
            throw new MigrationGapException("oauth-origin-invalid");
        return origin;
    }

    private static bool SameOrigin(Uri left, Uri right) =>
        string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.IdnHost, right.IdnHost, StringComparison.OrdinalIgnoreCase) && left.Port == right.Port;

    private static bool IsOpaqueFlowReference(string? value) =>
        value is { Length: >= 32 and <= 1024 } && value.All(static character => character is
            >= 'a' and <= 'z' or
            >= 'A' and <= 'Z' or
            >= '0' and <= '9' or
            '-' or '_');

    private static bool FixedTimeEquals(string first, string second)
    {
        var firstBytes = Encoding.UTF8.GetBytes(first);
        var secondBytes = Encoding.UTF8.GetBytes(second);
        return firstBytes.Length == secondBytes.Length &&
               CryptographicOperations.FixedTimeEquals(firstBytes, secondBytes);
    }

    private readonly record struct ScopedIdempotency(
        TenantId Tenant,
        WorkspaceId Workspace,
        PrincipalRef Principal,
        string Idempotency);
    private readonly record struct ScopedCommand(
        TenantId Tenant,
        WorkspaceId Workspace,
        PrincipalRef Principal,
        string CommandId);
    private sealed record OperationReceipt(
        string OperationId,
        string CommandType,
        int CommandVersion,
        string InputFingerprint,
        TenantId Tenant,
        WorkspaceId Workspace,
        PrincipalRef Principal,
        string CommandId);
}
