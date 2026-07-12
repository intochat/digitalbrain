using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.Core;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.Configuration;
using Orleans;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

internal static class InoTelemetry
{
    public static readonly ActivitySource Source = new("DigitalBrain.Mcp");
}

/// <summary>Durable, principal-scoped conversation journal used only by the INO runtime path.</summary>
public sealed class InoEffectStore : IInoConversationStore
{
    private const int JournalVersion = 4;
    private const string JournalDomain = "digitalbrain.v2.ino-effects";
    private const string SnapshotRecordKind = "conversation.snapshot";
    private const int MaximumAssistantCharacters = 16_000;
    private const string InterruptedReason = "I couldn’t confirm the previous response. You can continue from here.";
    private readonly ConcurrentDictionary<ConversationScope, InoConversationSnapshot> _conversations = new();
    private readonly HashSet<ConversationScope> _migrationScopes = [];
    private readonly AuthenticatedJsonLinesJournal? _journal;
    private readonly ToolActionPolicy _actionPolicy;
    private readonly object _gate = new();

    public InoEffectStore(
        string? path = null,
        ToolActionPolicy? actionPolicy = null,
        byte[]? journalIntegrityKey = null)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (journalIntegrityKey is null || journalIntegrityKey.Length < 32)
                throw new ArgumentException(
                    "A stable journal integrity key of at least 256 bits is required for durable INO conversations.",
                    nameof(journalIntegrityKey));
            _journal = new AuthenticatedJsonLinesJournal(JournalDomain, journalIntegrityKey, path);
        }
        _actionPolicy = actionPolicy ?? new ToolActionPolicy();
        Load();
        RecoverObsoleteConnectionActions();
        RecoverInterruptedConversations();
        PersistMigrations();
    }

    public InoConversationSnapshot Read(RuntimeRequestContext context)
    {
        lock (_gate)
            return _conversations.TryGetValue(Scope(context), out var snapshot)
                ? Clone(snapshot)
                : InoConversationSnapshot.Empty(context);
    }

    public InoConversationSnapshot Begin(RuntimeRequestContext context, string commandId, string prompt)
    {
        if (string.IsNullOrWhiteSpace(commandId) || commandId.Length > 1024)
            throw new ArgumentException("A bounded command id is required.", nameof(commandId));
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 4096)
            throw new ArgumentException("A bounded prompt is required.", nameof(prompt));

        lock (_gate)
        {
            var scope = Scope(context);
            var current = _conversations.GetValueOrDefault(scope) ?? InoConversationSnapshot.Empty(context);
            var existing = current.Operations.FirstOrDefault(operation =>
                string.Equals(operation.CommandId, commandId, StringComparison.Ordinal));
            if (existing is not null)
            {
                if (!string.Equals(existing.Prompt, prompt, StringComparison.Ordinal))
                    throw new InvalidOperationException("A conversation command cannot change its prompt.");
                return Clone(current);
            }
            var now = DateTimeOffset.UtcNow;
            var next = current with
            {
                Turns = current.Turns.Concat([
                    new InoConversationTurn(commandId, "user", prompt.Trim(), InoConversationStates.Queued)
                ]).ToArray(),
                Operations = current.Operations.Concat([
                    new InoConversationOperation(commandId, prompt.Trim(), InoConversationStates.Queued, null, false, now)
                ]).ToArray()
            };
            next = PruneCompletedEntries(next, commandId);
            DemandWithinPayloadBudget(next);
            return Persist(scope, next);
        }
    }

    public InoConversationSnapshot Transition(RuntimeRequestContext context, string commandId, string state)
    {
        if (state is not (InoConversationStates.Running or InoConversationStates.Responding))
            throw new ArgumentOutOfRangeException(nameof(state));
        lock (_gate)
        {
            var scope = Scope(context);
            var current = Required(scope, commandId);
            var operation = current.Operations.Single(candidate =>
                string.Equals(candidate.CommandId, commandId, StringComparison.Ordinal));
            if (string.Equals(operation.State, state, StringComparison.Ordinal)) return Clone(current);
            var validPrior = state == InoConversationStates.Running
                ? operation.State is InoConversationStates.Queued or InoConversationStates.AwaitingAuthorization
                : string.Equals(operation.State, InoConversationStates.Running, StringComparison.Ordinal);
            if (!validPrior)
                throw new InvalidOperationException("The conversation state transition is out of order.");

            var transitionBase = current with
            {
                Turns = current.Turns.Select(turn =>
                    string.Equals(turn.CommandId, commandId, StringComparison.Ordinal) &&
                    string.Equals(turn.Role, "assistant", StringComparison.Ordinal)
                        ? turn with { State = state }
                        : turn).ToArray()
            };
            var next = ReplaceOperationAndUserTurn(transitionBase, operation with
            {
                State = state,
                SafeReason = null,
                UpdatedAt = DateTimeOffset.UtcNow
            }, state);
            return Persist(scope, next);
        }
    }

    public InoConversationSnapshot AwaitAuthorization(
        RuntimeRequestContext context,
        string commandId,
        string response,
        ToolAction action,
        ExternalAuthorizationContinuation authorization)
    {
        if (string.IsNullOrWhiteSpace(response))
            throw new ArgumentException("A non-empty assistant response is required.", nameof(response));
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(authorization);
        if (!_actionPolicy.IsAllowed(action))
            throw new InvalidOperationException("The external authorization action is not allowed.");
        if (!authorization.IsValid())
            throw new InvalidOperationException("The external authorization continuation is invalid.");

        lock (_gate)
        {
            var scope = Scope(context);
            var current = Required(scope, commandId);
            var operation = current.Operations.Single(candidate =>
                string.Equals(candidate.CommandId, commandId, StringComparison.Ordinal));
            if (!string.Equals(operation.State, InoConversationStates.Responding, StringComparison.Ordinal))
                throw new InvalidOperationException("Authorization cannot be awaited before responding begins.");

            var safeResponse = BoundCharacters(response.Trim(), MaximumAssistantCharacters);
            var hasAssistant = current.Turns.Any(turn =>
                string.Equals(turn.CommandId, commandId, StringComparison.Ordinal) &&
                string.Equals(turn.Role, "assistant", StringComparison.Ordinal));
            var candidate = current with
            {
                Turns = hasAssistant
                    ? current.Turns.Select(turn =>
                        string.Equals(turn.CommandId, commandId, StringComparison.Ordinal) &&
                        string.Equals(turn.Role, "assistant", StringComparison.Ordinal)
                            ? turn with { Text = safeResponse, State = InoConversationStates.AwaitingAuthorization }
                            : turn).ToArray()
                    : current.Turns.Concat([
                        new InoConversationTurn(
                            commandId,
                            "assistant",
                            safeResponse,
                            InoConversationStates.AwaitingAuthorization)
                    ]).ToArray()
            };
            var next = ReplaceOperationAndUserTurn(candidate, operation with
            {
                State = InoConversationStates.AwaitingAuthorization,
                SafeReason = null,
                Retryable = true,
                Action = action,
                Grounding = null,
                Groundings = null,
                Authorization = authorization.Copy(),
                UpdatedAt = DateTimeOffset.UtcNow
            }, InoConversationStates.AwaitingAuthorization);
            next = FitAssistantResponse(next, commandId, safeResponse);
            return Persist(scope, next);
        }
    }

    public InoConversationSnapshot Complete(
        RuntimeRequestContext context,
        string commandId,
        string response,
        ToolAction? action = null,
        ToolGrounding? grounding = null,
        IReadOnlyList<ToolGrounding>? groundings = null)
    {
        if (string.IsNullOrWhiteSpace(response))
            throw new ArgumentException("A non-empty assistant response is required.", nameof(response));
        lock (_gate)
        {
            var scope = Scope(context);
            var current = Required(scope, commandId);
            var operation = current.Operations.Single(candidate =>
                string.Equals(candidate.CommandId, commandId, StringComparison.Ordinal));
            if (string.Equals(operation.State, InoConversationStates.Succeeded, StringComparison.Ordinal))
                return Clone(current);
            if (!string.Equals(operation.State, InoConversationStates.Responding, StringComparison.Ordinal))
                throw new InvalidOperationException("The assistant response cannot complete before responding begins.");

            var safeResponse = BoundCharacters(response.Trim(), MaximumAssistantCharacters);
            var hasAssistant = current.Turns.Any(turn =>
                string.Equals(turn.CommandId, commandId, StringComparison.Ordinal) &&
                string.Equals(turn.Role, "assistant", StringComparison.Ordinal));
            var candidate = current with
            {
                Turns = hasAssistant
                    ? current.Turns.Select(turn =>
                        string.Equals(turn.CommandId, commandId, StringComparison.Ordinal) &&
                        string.Equals(turn.Role, "assistant", StringComparison.Ordinal)
                            ? turn with { Text = safeResponse, State = InoConversationStates.Succeeded }
                            : turn).ToArray()
                    : current.Turns.Concat([
                        new InoConversationTurn(
                            commandId,
                            "assistant",
                            safeResponse,
                            InoConversationStates.Succeeded)
                    ]).ToArray()
            };
            candidate = PruneCompletedEntries(candidate, commandId);
            operation = candidate.Operations.Single(candidateOperation =>
                string.Equals(candidateOperation.CommandId, commandId, StringComparison.Ordinal));
            var next = ReplaceOperationAndUserTurn(candidate, operation with
            {
                State = InoConversationStates.Succeeded,
                SafeReason = null,
                Retryable = false,
                Action = action,
                Grounding = grounding is null
                    ? null
                    : new ToolGrounding(grounding.ToolId, grounding.Content.Clone()),
                Groundings = groundings?.Select(static value =>
                    new ToolGrounding(value.ToolId, value.Content.Clone())).ToArray(),
                Authorization = null,
                UpdatedAt = DateTimeOffset.UtcNow
            }, InoConversationStates.Succeeded);
            next = FitAssistantResponse(next, commandId, safeResponse);
            return Persist(scope, next);
        }
    }

    public InoConversationSnapshot Fail(
        RuntimeRequestContext context,
        string commandId,
        string safeReason,
        bool retryable)
    {
        lock (_gate)
        {
            var scope = Scope(context);
            var current = Required(scope, commandId);
            var operation = current.Operations.Single(candidate =>
                string.Equals(candidate.CommandId, commandId, StringComparison.Ordinal));
            if (string.Equals(operation.State, InoConversationStates.Succeeded, StringComparison.Ordinal) ||
                string.Equals(operation.State, InoConversationStates.Failed, StringComparison.Ordinal))
                return Clone(current);
            var reason = string.IsNullOrWhiteSpace(safeReason)
                ? "I couldn’t finish that response."
                : safeReason.Trim();
            if (reason.Length > 256) reason = reason[..256] + "…";
            var next = ReplaceOperationAndUserTurn(current, operation with
            {
                State = InoConversationStates.Failed,
                SafeReason = reason,
                Retryable = retryable,
                Action = null,
                Authorization = null,
                UpdatedAt = DateTimeOffset.UtcNow
            }, "failed");
            return Persist(scope, next);
        }
    }

    private InoConversationSnapshot Required(ConversationScope scope, string commandId)
    {
        if (!_conversations.TryGetValue(scope, out var snapshot) ||
            !snapshot.Operations.Any(operation => string.Equals(operation.CommandId, commandId, StringComparison.Ordinal)))
            throw new InvalidOperationException("The conversation operation was not journaled.");
        return snapshot;
    }

    private InoConversationSnapshot Persist(ConversationScope scope, InoConversationSnapshot snapshot)
    {
        DemandWithinDeliveryBound(snapshot);
        var next = snapshot with
        {
            Revision = checked(snapshot.Revision + 1),
            Turns = snapshot.Turns.ToArray(),
            Operations = snapshot.Operations.ToArray()
        };
        Append(new PersistedConversation(JournalVersion, scope.Tenant, scope.Workspace, scope.Principal, next));
        _conversations[scope] = next;
        return Clone(next);
    }

    private void Append(PersistedConversation record)
    {
        _journal?.Append(SnapshotRecordKind, JsonSerializer.Serialize(record));
        if (record.Version == JournalVersion)
            _migrationScopes.Remove(new ConversationScope(record.Tenant, record.Workspace, record.Principal));
    }

    private void Load()
    {
        if (_journal is null) return;
        foreach (var record in _journal.Read())
        {
            PersistedConversation? persisted;
            try { persisted = JsonSerializer.Deserialize<PersistedConversation>(record.Payload); }
            catch (JsonException)
            {
                throw _journal.Invalid(
                    record,
                    "invalid-conversation-json",
                    $"The INO conversation journal contains malformed JSON at line {record.LineNumber}.");
            }
            if (!record.IsLegacy && !string.Equals(record.Kind, SnapshotRecordKind, StringComparison.Ordinal))
                throw _journal.Invalid(
                    record,
                    "invalid-conversation-kind",
                    $"The INO conversation journal contains an unexpected record kind at line {record.LineNumber}.");
            if (!TryNormalize(persisted, out var normalized))
                throw _journal.Invalid(
                    record,
                    "invalid-conversation-record",
                    $"The INO conversation journal contains an invalid record at line {record.LineNumber}.");
            var scope = new ConversationScope(normalized.Tenant, normalized.Workspace, normalized.Principal);
            var requiresMigration = persisted!.Version != JournalVersion;
            if (!_conversations.TryGetValue(scope, out var current) || normalized.Snapshot.Revision > current.Revision)
            {
                _conversations[scope] = Clone(normalized.Snapshot);
                if (requiresMigration) _migrationScopes.Add(scope);
                else _migrationScopes.Remove(scope);
            }
            else if (normalized.Snapshot.Revision == current.Revision && !requiresMigration)
            {
                _migrationScopes.Remove(scope);
            }
        }
        _journal.SealLegacy();
    }

    private void PersistMigrations()
    {
        foreach (var scope in _migrationScopes.ToArray())
            if (_conversations.TryGetValue(scope, out var snapshot))
                Append(new PersistedConversation(
                    JournalVersion,
                    scope.Tenant,
                    scope.Workspace,
                    scope.Principal,
                    snapshot));
        _migrationScopes.Clear();
    }

    private void RecoverInterruptedConversations()
    {
        lock (_gate)
        {
            foreach (var pair in _conversations.ToArray())
            {
                var active = pair.Value.Operations
                    .Where(operation => InoConversationStates.IsActive(operation.State))
                    .Select(static operation => operation.CommandId)
                    .ToHashSet(StringComparer.Ordinal);
                if (active.Count == 0) continue;
                var resumableAuthorizations = pair.Value.Operations
                    .Where(operation => active.Contains(operation.CommandId) &&
                                        operation.Action is not null &&
                                        operation.Authorization?.IsValid() == true &&
                                        _actionPolicy.IsAllowed(operation.Action))
                    .Select(static operation => operation.CommandId)
                    .ToHashSet(StringComparer.Ordinal);
                var now = DateTimeOffset.UtcNow;
                var recovered = pair.Value with
                {
                    Revision = checked(pair.Value.Revision + 1),
                    Turns = pair.Value.Turns
                        .Where(turn => resumableAuthorizations.Contains(turn.CommandId) ||
                                       !active.Contains(turn.CommandId) || turn.Role != "assistant")
                        .Select(turn => resumableAuthorizations.Contains(turn.CommandId)
                            ? turn with { State = InoConversationStates.AwaitingAuthorization }
                            : active.Contains(turn.CommandId)
                                ? turn with { State = InoConversationStates.Failed }
                            : turn)
                        .ToArray(),
                    Operations = pair.Value.Operations.Select(operation => active.Contains(operation.CommandId)
                        ? operation with
                        {
                            State = resumableAuthorizations.Contains(operation.CommandId)
                                ? InoConversationStates.AwaitingAuthorization
                                : InoConversationStates.Failed,
                            SafeReason = resumableAuthorizations.Contains(operation.CommandId)
                                ? null
                                : InterruptedReason,
                            Retryable = resumableAuthorizations.Contains(operation.CommandId),
                            Action = resumableAuthorizations.Contains(operation.CommandId)
                                ? operation.Action
                                : null,
                            Authorization = resumableAuthorizations.Contains(operation.CommandId)
                                ? operation.Authorization?.Copy()
                                : null,
                            Grounding = null,
                            Groundings = null,
                            UpdatedAt = now
                        }
                        : operation).ToArray()
                };
                DemandWithinDeliveryBound(recovered);
                Append(new PersistedConversation(JournalVersion, pair.Key.Tenant, pair.Key.Workspace, pair.Key.Principal, recovered));
                _conversations[pair.Key] = Clone(recovered);
            }
        }
    }

    private void RecoverObsoleteConnectionActions()
    {
        lock (_gate)
        {
            foreach (var pair in _conversations.ToArray())
            {
                if (!pair.Value.Operations.Any(operation =>
                        operation.Action is not null && !_actionPolicy.IsAllowed(operation.Action)))
                    continue;

                Persist(pair.Key, pair.Value with
                {
                    Operations = pair.Value.Operations.Select(operation =>
                        operation.Action is not null && !_actionPolicy.IsAllowed(operation.Action)
                            ? operation with { Action = null, Authorization = null }
                            : operation).ToArray()
                });
            }
        }
    }

    private static InoConversationSnapshot ReplaceOperationAndUserTurn(
        InoConversationSnapshot snapshot,
        InoConversationOperation replacement,
        string turnState) =>
        snapshot with
        {
            Turns = snapshot.Turns.Select(turn =>
                string.Equals(turn.CommandId, replacement.CommandId, StringComparison.Ordinal) &&
                string.Equals(turn.Role, "user", StringComparison.Ordinal)
                    ? turn with { State = turnState }
                    : turn).ToArray(),
            Operations = snapshot.Operations.Select(operation =>
                string.Equals(operation.CommandId, replacement.CommandId, StringComparison.Ordinal)
                    ? replacement
                    : operation).ToArray()
        };

    private bool Valid(PersistedConversation? persisted)
    {
        if (persisted is null || persisted.Version != JournalVersion ||
            string.IsNullOrWhiteSpace(persisted.Tenant.Value) ||
            string.IsNullOrWhiteSpace(persisted.Workspace.Value) ||
            string.IsNullOrWhiteSpace(persisted.Principal.Value) ||
            persisted.Snapshot is null ||
            string.IsNullOrWhiteSpace(persisted.Snapshot.ConversationId) ||
            persisted.Snapshot.Revision < 0 || persisted.Snapshot.Turns.Count > 200 ||
            persisted.Snapshot.Operations.Count > 128 ||
            persisted.Snapshot.Operations.Any(operation => operation.Authorization is not null &&
                (!operation.Authorization.IsValid() ||
                 operation.State is not (InoConversationStates.AwaitingAuthorization or
                     InoConversationStates.Running or InoConversationStates.Responding) ||
                 operation.Action is null || !_actionPolicy.IsAllowed(operation.Action))))
            return false;
        var context = new RuntimeRequestContext(persisted.Tenant, persisted.Workspace, persisted.Principal,
            "journal-validation", AuthAssurance.Password, "journal-validation", null, new HashSet<string>());
        var payloadBytes = PayloadBytes(persisted.Snapshot);
        return string.Equals(persisted.Snapshot.ConversationId, InoConversationIdentity.From(context), StringComparison.Ordinal) &&
               payloadBytes <= PrivateFeedStore.MaximumSurfacePayloadBytes &&
               JournalPayloadBytes(persisted.Snapshot) <= PrivateFeedStore.MaximumSurfacePayloadBytes &&
               (!persisted.Snapshot.Operations.Any(operation => InoConversationStates.IsActive(operation.State)) ||
                payloadBytes <= WorkspaceSurfaceProducer.InoPayloadBudgetBytes);
    }

    private bool TryNormalize(
        PersistedConversation? persisted,
        out PersistedConversation normalized)
    {
        normalized = persisted!;
        if (persisted is not { Version: 2 or 3 or JournalVersion } || persisted.Snapshot is null)
            return false;
        if (persisted.Version == JournalVersion)
            return Valid(persisted);

        var scrubbedActions = persisted.Snapshot.Operations.Any(operation =>
            !InoConversationStates.IsActive(operation.State) &&
            (operation.Action is not null || operation.Authorization is not null));
        var migrated = persisted.Snapshot with
        {
            Operations = persisted.Snapshot.Operations.Select(operation =>
                !InoConversationStates.IsActive(operation.State)
                    ? operation with { Action = null, Authorization = null }
                    : operation).ToArray()
        };
        var bounded = PruneCompletedEntries(migrated, string.Empty);
        var changed = scrubbedActions ||
                      bounded.Operations.Count != persisted.Snapshot.Operations.Count ||
                      bounded.Turns.Count != persisted.Snapshot.Turns.Count;
        if (changed)
        {
            if (bounded.Revision == int.MaxValue) return false;
            bounded = bounded with { Revision = bounded.Revision + 1 };
        }
        normalized = persisted with { Version = JournalVersion, Snapshot = bounded };
        return Valid(normalized);
    }

    private static InoConversationSnapshot PruneCompletedEntries(
        InoConversationSnapshot snapshot,
        string preservedCommandId)
    {
        var next = snapshot;
        while (!WithinRetentionAndPayloadBudget(next))
        {
            var removable = next.Operations.FirstOrDefault(operation =>
                !string.Equals(operation.CommandId, preservedCommandId, StringComparison.Ordinal) &&
                !InoConversationStates.IsActive(operation.State));
            if (removable is null) break;
            next = next with
            {
                Turns = next.Turns.Where(turn =>
                    !string.Equals(turn.CommandId, removable.CommandId, StringComparison.Ordinal)).ToArray(),
                Operations = next.Operations.Where(operation =>
                    !string.Equals(operation.CommandId, removable.CommandId, StringComparison.Ordinal)).ToArray()
            };
        }
        return next;
    }

    private static InoConversationSnapshot FitAssistantResponse(
        InoConversationSnapshot snapshot,
        string commandId,
        string response)
    {
        if (PayloadBytes(snapshot) <= WorkspaceSurfaceProducer.InoPayloadBudgetBytes)
            return snapshot;

        var low = 0;
        var high = response.Length;
        InoConversationSnapshot? best = null;
        while (low <= high)
        {
            var requestedLength = low + ((high - low) / 2);
            var prefixLength = SafePrefixLength(response, requestedLength);
            var text = prefixLength == response.Length
                ? response
                : response[..prefixLength] + "…";
            var candidate = ReplaceAssistantText(snapshot, commandId, text);
            if (PayloadBytes(candidate) <= WorkspaceSurfaceProducer.InoPayloadBudgetBytes)
            {
                best = candidate;
                low = requestedLength + 1;
            }
            else
            {
                high = requestedLength - 1;
            }
        }

        return best ?? throw new InvalidOperationException("The assistant response cannot fit the durable conversation surface.");
    }

    private static InoConversationSnapshot ReplaceAssistantText(
        InoConversationSnapshot snapshot,
        string commandId,
        string text) =>
        snapshot with
        {
            Turns = snapshot.Turns.Select(turn =>
                string.Equals(turn.CommandId, commandId, StringComparison.Ordinal) &&
                string.Equals(turn.Role, "assistant", StringComparison.Ordinal)
                    ? turn with { Text = text }
                    : turn).ToArray()
        };

    private static string BoundCharacters(string value, int maximumCharacters)
    {
        if (value.Length <= maximumCharacters) return value;
        var prefixLength = SafePrefixLength(value, maximumCharacters);
        return value[..prefixLength] + "…";
    }

    private static int SafePrefixLength(string value, int requestedLength)
    {
        var length = Math.Clamp(requestedLength, 0, value.Length);
        if (length > 0 && length < value.Length &&
            char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length]))
            length--;
        return length;
    }

    private static bool WithinRetentionAndPayloadBudget(InoConversationSnapshot snapshot) =>
        snapshot.Turns.Count <= 200 && snapshot.Operations.Count <= 128 &&
        PayloadBytes(snapshot) <= WorkspaceSurfaceProducer.InoPayloadBudgetBytes &&
        JournalPayloadBytes(snapshot) <= PrivateFeedStore.MaximumSurfacePayloadBytes;

    private static void DemandWithinPayloadBudget(InoConversationSnapshot snapshot)
    {
        if (snapshot.Turns.Count > 200 || snapshot.Operations.Count > 128 ||
            PayloadBytes(snapshot) > WorkspaceSurfaceProducer.InoPayloadBudgetBytes ||
            JournalPayloadBytes(snapshot) > PrivateFeedStore.MaximumSurfacePayloadBytes)
            throw new InvalidOperationException("The conversation exceeds its durable presentation bound.");
    }

    private static void DemandWithinDeliveryBound(InoConversationSnapshot snapshot)
    {
        if (snapshot.Turns.Count > 200 || snapshot.Operations.Count > 128 ||
            PayloadBytes(snapshot) > PrivateFeedStore.MaximumSurfacePayloadBytes ||
            JournalPayloadBytes(snapshot) > PrivateFeedStore.MaximumSurfacePayloadBytes)
            throw new InvalidOperationException("The conversation exceeds its durable delivery bound.");
    }

    private static int PayloadBytes(InoConversationSnapshot snapshot) =>
        Encoding.UTF8.GetByteCount(WorkspaceSurfaceProducer.BuildInoPayload(snapshot).GetRawText());

    private static int JournalPayloadBytes(InoConversationSnapshot snapshot) =>
        Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(snapshot));

    private static InoConversationSnapshot Clone(InoConversationSnapshot snapshot) => snapshot with
    {
        Turns = snapshot.Turns.ToArray(),
        Operations = snapshot.Operations.Select(static operation => operation with
        {
            Grounding = operation.Grounding is null
                ? null
                : new ToolGrounding(operation.Grounding.ToolId, operation.Grounding.Content.Clone()),
            Groundings = operation.Groundings?.Select(static grounding =>
                new ToolGrounding(grounding.ToolId, grounding.Content.Clone())).ToArray(),
            Authorization = operation.Authorization?.Copy()
        }).ToArray()
    };

    private static ConversationScope Scope(RuntimeRequestContext context) =>
        new(context.TenantId, context.WorkspaceId, context.Principal);

    private readonly record struct ConversationScope(TenantId Tenant, WorkspaceId Workspace, PrincipalRef Principal);
    private sealed record PersistedConversation(
        int Version,
        TenantId Tenant,
        WorkspaceId Workspace,
        PrincipalRef Principal,
        InoConversationSnapshot Snapshot);
}

/// <summary>Authenticated conversation command; all identity and model authority remain server-derived.</summary>
