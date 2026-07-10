using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.Core.V2;
using DigitalBrain.Kernel.V2;
using Orleans;
using V2RequestContext = DigitalBrain.Core.V2.RequestContext;

namespace DigitalBrain.Mcp;

/// <summary>Durable, principal-scoped conversation journal used only by the V2 INO path.</summary>
public sealed class V2InoEffectStore : IV2InoConversationStore
{
    private const int JournalVersion = 3;
    private const int MaximumAssistantCharacters = 16_000;
    private const string InterruptedReason = "I couldn’t confirm the previous response. You can continue from here.";
    private readonly ConcurrentDictionary<ConversationScope, V2InoConversationSnapshot> _conversations = new();
    private readonly string? _path;
    private readonly object _gate = new();

    public V2InoEffectStore(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        Load();
        RecoverInterruptedConversations();
    }

    public V2InoConversationSnapshot Read(V2RequestContext context)
    {
        lock (_gate)
            return _conversations.TryGetValue(Scope(context), out var snapshot)
                ? Clone(snapshot)
                : V2InoConversationSnapshot.Empty(context);
    }

    public V2InoConversationSnapshot Begin(V2RequestContext context, string commandId, string prompt)
    {
        if (string.IsNullOrWhiteSpace(commandId) || commandId.Length > 1024)
            throw new ArgumentException("A bounded command id is required.", nameof(commandId));
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 4096)
            throw new ArgumentException("A bounded prompt is required.", nameof(prompt));

        lock (_gate)
        {
            var scope = Scope(context);
            var current = _conversations.GetValueOrDefault(scope) ?? V2InoConversationSnapshot.Empty(context);
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
                    new V2InoConversationTurn(commandId, "user", prompt.Trim(), V2InoConversationStates.Queued)
                ]).ToArray(),
                Operations = current.Operations.Concat([
                    new V2InoConversationOperation(commandId, prompt.Trim(), V2InoConversationStates.Queued, null, false, now)
                ]).ToArray()
            };
            next = PruneCompletedEntries(next, commandId);
            DemandWithinPayloadBudget(next);
            return Persist(scope, next);
        }
    }

    public V2InoConversationSnapshot Transition(V2RequestContext context, string commandId, string state)
    {
        if (state is not (V2InoConversationStates.Running or V2InoConversationStates.Responding))
            throw new ArgumentOutOfRangeException(nameof(state));
        lock (_gate)
        {
            var scope = Scope(context);
            var current = Required(scope, commandId);
            var operation = current.Operations.Single(candidate =>
                string.Equals(candidate.CommandId, commandId, StringComparison.Ordinal));
            if (string.Equals(operation.State, state, StringComparison.Ordinal)) return Clone(current);
            var expected = state == V2InoConversationStates.Running
                ? V2InoConversationStates.Queued
                : V2InoConversationStates.Running;
            if (!string.Equals(operation.State, expected, StringComparison.Ordinal))
                throw new InvalidOperationException("The conversation state transition is out of order.");

            var next = ReplaceOperationAndUserTurn(current, operation with
            {
                State = state,
                UpdatedAt = DateTimeOffset.UtcNow
            }, state);
            return Persist(scope, next);
        }
    }

    public V2InoConversationSnapshot Complete(
        V2RequestContext context,
        string commandId,
        string response,
        V2ToolAction? action = null)
    {
        if (string.IsNullOrWhiteSpace(response))
            throw new ArgumentException("A non-empty assistant response is required.", nameof(response));
        lock (_gate)
        {
            var scope = Scope(context);
            var current = Required(scope, commandId);
            var operation = current.Operations.Single(candidate =>
                string.Equals(candidate.CommandId, commandId, StringComparison.Ordinal));
            if (string.Equals(operation.State, V2InoConversationStates.Succeeded, StringComparison.Ordinal))
                return Clone(current);
            if (!string.Equals(operation.State, V2InoConversationStates.Responding, StringComparison.Ordinal))
                throw new InvalidOperationException("The assistant response cannot complete before responding begins.");

            var safeResponse = BoundCharacters(response.Trim(), MaximumAssistantCharacters);
            var withAssistant = current;
            if (!current.Turns.Any(turn =>
                    string.Equals(turn.CommandId, commandId, StringComparison.Ordinal) &&
                    string.Equals(turn.Role, "assistant", StringComparison.Ordinal)))
            {
                var candidate = current with
                {
                    Turns = current.Turns.Concat([
                        new V2InoConversationTurn(
                            commandId,
                            "assistant",
                            safeResponse,
                            V2InoConversationStates.Responding)
                    ]).ToArray()
                };
                candidate = PruneCompletedEntries(candidate, commandId);
                candidate = FitAssistantResponse(candidate, commandId, safeResponse);
                withAssistant = Persist(scope, candidate);
            }

            operation = withAssistant.Operations.Single(candidate =>
                string.Equals(candidate.CommandId, commandId, StringComparison.Ordinal));
            var next = ReplaceOperationAndUserTurn(withAssistant, operation with
            {
                State = V2InoConversationStates.Succeeded,
                SafeReason = null,
                Retryable = false,
                Action = action,
                UpdatedAt = DateTimeOffset.UtcNow
            }, V2InoConversationStates.Succeeded);
            next = next with
            {
                Turns = next.Turns.Select(turn =>
                    string.Equals(turn.CommandId, commandId, StringComparison.Ordinal) &&
                    string.Equals(turn.Role, "assistant", StringComparison.Ordinal)
                        ? turn with { State = V2InoConversationStates.Succeeded }
                        : turn).ToArray()
            };
            return Persist(scope, next);
        }
    }

    public V2InoConversationSnapshot Fail(
        V2RequestContext context,
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
            if (string.Equals(operation.State, V2InoConversationStates.Succeeded, StringComparison.Ordinal) ||
                string.Equals(operation.State, V2InoConversationStates.Failed, StringComparison.Ordinal))
                return Clone(current);
            var reason = string.IsNullOrWhiteSpace(safeReason)
                ? "I couldn’t finish that response."
                : safeReason.Trim();
            if (reason.Length > 256) reason = reason[..256] + "…";
            var next = ReplaceOperationAndUserTurn(current, operation with
            {
                State = V2InoConversationStates.Failed,
                SafeReason = reason,
                Retryable = retryable,
                Action = null,
                UpdatedAt = DateTimeOffset.UtcNow
            }, "failed");
            return Persist(scope, next);
        }
    }

    private V2InoConversationSnapshot Required(ConversationScope scope, string commandId)
    {
        if (!_conversations.TryGetValue(scope, out var snapshot) ||
            !snapshot.Operations.Any(operation => string.Equals(operation.CommandId, commandId, StringComparison.Ordinal)))
            throw new InvalidOperationException("The conversation operation was not journaled.");
        return snapshot;
    }

    private V2InoConversationSnapshot Persist(ConversationScope scope, V2InoConversationSnapshot snapshot)
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
        if (_path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.AppendAllText(_path, JsonSerializer.Serialize(record) + Environment.NewLine);
    }

    private void Load()
    {
        if (_path is null || !File.Exists(_path)) return;
        foreach (var line in File.ReadLines(_path).Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            PersistedConversation? persisted;
            try { persisted = JsonSerializer.Deserialize<PersistedConversation>(line); }
            catch (JsonException) { continue; }
            if (!Valid(persisted)) continue;
            var scope = new ConversationScope(persisted!.Tenant, persisted.Workspace, persisted.Principal);
            if (!_conversations.TryGetValue(scope, out var current) || persisted.Snapshot.Revision > current.Revision)
                _conversations[scope] = Clone(persisted.Snapshot);
        }
    }

    private void RecoverInterruptedConversations()
    {
        lock (_gate)
        {
            foreach (var pair in _conversations.ToArray())
            {
                var active = pair.Value.Operations
                    .Where(operation => V2InoConversationStates.IsActive(operation.State))
                    .Select(static operation => operation.CommandId)
                    .ToHashSet(StringComparer.Ordinal);
                if (active.Count == 0) continue;
                var answered = pair.Value.Turns
                    .Where(turn => active.Contains(turn.CommandId) && turn.Role == "assistant")
                    .Select(static turn => turn.CommandId)
                    .ToHashSet(StringComparer.Ordinal);
                var now = DateTimeOffset.UtcNow;
                var recovered = pair.Value with
                {
                    Revision = checked(pair.Value.Revision + 1),
                    Turns = pair.Value.Turns.Select(turn => active.Contains(turn.CommandId)
                        ? turn with
                        {
                            State = answered.Contains(turn.CommandId)
                                ? V2InoConversationStates.Succeeded
                                : V2InoConversationStates.Failed
                        }
                        : turn).ToArray(),
                    Operations = pair.Value.Operations.Select(operation => active.Contains(operation.CommandId)
                        ? operation with
                        {
                            State = answered.Contains(operation.CommandId)
                                ? V2InoConversationStates.Succeeded
                                : V2InoConversationStates.Failed,
                            SafeReason = answered.Contains(operation.CommandId) ? null : InterruptedReason,
                            Retryable = false,
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

    private static V2InoConversationSnapshot ReplaceOperationAndUserTurn(
        V2InoConversationSnapshot snapshot,
        V2InoConversationOperation replacement,
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

    private static bool Valid(PersistedConversation? persisted)
    {
        if (persisted is null || persisted.Version != JournalVersion ||
            string.IsNullOrWhiteSpace(persisted.Tenant.Value) ||
            string.IsNullOrWhiteSpace(persisted.Workspace.Value) ||
            string.IsNullOrWhiteSpace(persisted.Principal.Value) ||
            persisted.Snapshot is null ||
            string.IsNullOrWhiteSpace(persisted.Snapshot.ConversationId) ||
            persisted.Snapshot.Revision < 0 || persisted.Snapshot.Turns.Count > 200 ||
            persisted.Snapshot.Operations.Count > 128)
            return false;
        var context = new V2RequestContext(persisted.Tenant, persisted.Workspace, persisted.Principal,
            "journal-validation", AuthAssurance.Password, "journal-validation", null, new HashSet<string>());
        var payloadBytes = PayloadBytes(persisted.Snapshot);
        return string.Equals(persisted.Snapshot.ConversationId, V2InoConversationIdentity.From(context), StringComparison.Ordinal) &&
               payloadBytes <= V2PrivateFeedStore.MaximumSurfacePayloadBytes &&
               (!persisted.Snapshot.Operations.Any(operation => V2InoConversationStates.IsActive(operation.State)) ||
                payloadBytes <= V2WorkspaceSurfaceProducer.InoPayloadBudgetBytes);
    }

    private static V2InoConversationSnapshot PruneCompletedEntries(
        V2InoConversationSnapshot snapshot,
        string preservedCommandId)
    {
        var next = snapshot;
        while (!WithinRetentionAndPayloadBudget(next))
        {
            var removable = next.Operations.FirstOrDefault(operation =>
                !string.Equals(operation.CommandId, preservedCommandId, StringComparison.Ordinal) &&
                !V2InoConversationStates.IsActive(operation.State));
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

    private static V2InoConversationSnapshot FitAssistantResponse(
        V2InoConversationSnapshot snapshot,
        string commandId,
        string response)
    {
        if (PayloadBytes(snapshot) <= V2WorkspaceSurfaceProducer.InoPayloadBudgetBytes)
            return snapshot;

        var low = 0;
        var high = response.Length;
        V2InoConversationSnapshot? best = null;
        while (low <= high)
        {
            var requestedLength = low + ((high - low) / 2);
            var prefixLength = SafePrefixLength(response, requestedLength);
            var text = prefixLength == response.Length
                ? response
                : response[..prefixLength] + "…";
            var candidate = ReplaceAssistantText(snapshot, commandId, text);
            if (PayloadBytes(candidate) <= V2WorkspaceSurfaceProducer.InoPayloadBudgetBytes)
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

    private static V2InoConversationSnapshot ReplaceAssistantText(
        V2InoConversationSnapshot snapshot,
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

    private static bool WithinRetentionAndPayloadBudget(V2InoConversationSnapshot snapshot) =>
        snapshot.Turns.Count <= 200 && snapshot.Operations.Count <= 128 &&
        PayloadBytes(snapshot) <= V2WorkspaceSurfaceProducer.InoPayloadBudgetBytes;

    private static void DemandWithinPayloadBudget(V2InoConversationSnapshot snapshot)
    {
        if (snapshot.Turns.Count > 200 || snapshot.Operations.Count > 128 ||
            PayloadBytes(snapshot) > V2WorkspaceSurfaceProducer.InoPayloadBudgetBytes)
            throw new InvalidOperationException("The conversation exceeds its durable presentation bound.");
    }

    private static void DemandWithinDeliveryBound(V2InoConversationSnapshot snapshot)
    {
        if (snapshot.Turns.Count > 200 || snapshot.Operations.Count > 128 ||
            PayloadBytes(snapshot) > V2PrivateFeedStore.MaximumSurfacePayloadBytes)
            throw new InvalidOperationException("The conversation exceeds its durable delivery bound.");
    }

    private static int PayloadBytes(V2InoConversationSnapshot snapshot) =>
        Encoding.UTF8.GetByteCount(V2WorkspaceSurfaceProducer.BuildInoPayload(snapshot).GetRawText());

    private static V2InoConversationSnapshot Clone(V2InoConversationSnapshot snapshot) => snapshot with
    {
        Turns = snapshot.Turns.ToArray(),
        Operations = snapshot.Operations.ToArray()
    };

    private static ConversationScope Scope(V2RequestContext context) =>
        new(context.TenantId, context.WorkspaceId, context.Principal);

    private readonly record struct ConversationScope(TenantId Tenant, WorkspaceId Workspace, PrincipalRef Principal);
    private sealed record PersistedConversation(
        int Version,
        TenantId Tenant,
        WorkspaceId Workspace,
        PrincipalRef Principal,
        V2InoConversationSnapshot Snapshot);
}

/// <summary>Authenticated V2 conversation command; all identity and model authority remain server-derived.</summary>
public sealed class V2McpInoCommandHandler(
    IV2InoConversationStore conversations,
    V2WorkspaceSurfaceProducer surfaces,
    V2ConversationOwner owner) : IV2CommandHandler
{
    private const string SafeFailure = "I couldn’t finish that response. Please try a new message.";
    public const string CommandType = "ino.interact";

    public bool CanHandle(string commandType) => string.Equals(commandType, CommandType, StringComparison.Ordinal);

    public async Task<V2CommandExecutionResult> ExecuteAsync(
        V2CommandEnvelope command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetPrompt(command.Payload, out var prompt))
            return new V2CommandExecutionResult(WorkflowState.Failed, "ino-request-invalid");

        var snapshot = conversations.Begin(command.Context, command.CommandId, prompt);
        var prior = snapshot.Operations.Single(operation =>
            string.Equals(operation.CommandId, command.CommandId, StringComparison.Ordinal));
        if (string.Equals(prior.State, V2InoConversationStates.Succeeded, StringComparison.Ordinal))
        {
            surfaces.PublishInoConversation(command.Context, snapshot);
            return V2CommandExecutionResult.Success();
        }
        if (string.Equals(prior.State, V2InoConversationStates.Failed, StringComparison.Ordinal))
        {
            surfaces.PublishInoConversation(command.Context, snapshot);
            return new V2CommandExecutionResult(WorkflowState.Failed, prior.SafeReason);
        }

        surfaces.PublishInoConversation(command.Context, snapshot);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            snapshot = conversations.Transition(command.Context, command.CommandId, V2InoConversationStates.Running);
            surfaces.PublishInoConversation(command.Context, snapshot);
            snapshot = conversations.Transition(command.Context, command.CommandId, V2InoConversationStates.Responding);
            surfaces.PublishInoConversation(command.Context, snapshot);

            var response = await owner.ExecuteDetailedAsync(new V2ConversationRequest(
                command.Context,
                snapshot.ConversationId,
                prompt,
                AllowTools: true), cancellationToken).ConfigureAwait(false);
            snapshot = conversations.Complete(command.Context, command.CommandId, response.Text, response.Action);
            surfaces.PublishInoConversation(command.Context, snapshot);
            return V2CommandExecutionResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            snapshot = conversations.Fail(command.Context, command.CommandId, SafeFailure, retryable: false);
            surfaces.PublishInoConversation(command.Context, snapshot);
            return new V2CommandExecutionResult(WorkflowState.Failed, SafeFailure);
        }
        catch (Exception)
        {
            snapshot = conversations.Fail(command.Context, command.CommandId, SafeFailure, retryable: false);
            surfaces.PublishInoConversation(command.Context, snapshot);
            return new V2CommandExecutionResult(WorkflowState.Failed, SafeFailure);
        }
    }

    public static bool TryGetPrompt(JsonElement payload, out string prompt)
    {
        prompt = string.Empty;
        if (payload.ValueKind != JsonValueKind.Object || payload.EnumerateObject().Count() != 1 ||
            !payload.TryGetProperty("prompt", out var value) || value.ValueKind != JsonValueKind.String)
            return false;
        prompt = value.GetString()?.Trim() ?? string.Empty;
        return prompt.Length is > 0 and <= 4096;
    }
}

public sealed class V2McpConversationContextAssembler(IV2InoConversationStore conversations) : IV2ContextAssembler
{
    public Task<V2ConversationContext> AssembleAsync(
        V2ConversationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = conversations.Read(request.Context);
        if (!string.Equals(snapshot.ConversationId, request.ConversationId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The conversation is outside the authenticated scope.");
        var history = snapshot.Turns
            .Where(turn => !(turn.Role == "user" && string.Equals(turn.Text, request.Text, StringComparison.Ordinal) &&
                             string.Equals(turn.CommandId, snapshot.CurrentOperation?.CommandId, StringComparison.Ordinal)))
            .TakeLast(12)
            .Select(static turn => $"{turn.Role}: {turn.Text}")
            .ToArray();
        return Task.FromResult(new V2ConversationContext(
            request.Context.TenantId,
            request.Context.WorkspaceId,
            request.ConversationId,
            history));
    }
}

public sealed class V2McpConversationModelRouter(IClusterClient cluster) : IV2ModelRouter
{
    public async Task<V2ModelResponse> CompleteAsync(
        V2ModelRequest request,
        CancellationToken cancellationToken = default)
    {
        var grainId = V2GrainIds.Conversation(
            request.Context.TenantId,
            request.Context.WorkspaceId,
            request.Context.ConversationId);
        var model = cluster.GetGrain<IV2ConversationModelGrain>(grainId);
        var response = await model.CompleteAsync(
            new V2ConversationModelCompletionRequest(
                request.Text,
                request.Context.MemoryEvidence,
                request.ToolOutcomes?.Select(static outcome => new V2ConversationModelToolOutcome(
                    outcome.Kind.ToString(),
                    outcome.Content?.GetRawText(),
                    outcome.SafeReason)).ToArray()),
            cancellationToken).ConfigureAwait(false);
        return new V2ModelResponse(response.Text, response.Model, IsStructured: false);
    }
}

public sealed class V2McpNoToolPlanner : IV2IntentCapabilityPlanner
{
    public Task<IReadOnlyList<V2ToolInvocation>> PlanAsync(
        V2ConversationRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<V2ToolInvocation>>([]);
}

public sealed class V2McpNoToolCatalog : IV2AuthorizedToolCatalog
{
    public Task<V2ToolOutcome> InvokeAsync(
        V2RequestContext context,
        V2ToolInvocation invocation,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new V2ToolOutcome(V2ToolOutcomeKind.Denied, SafeReason: "Tools are unavailable in this conversation."));
}

public sealed class V2McpIntegrationPlanner : IV2IntentCapabilityPlanner
{
    public Task<IReadOnlyList<V2ToolInvocation>> PlanAsync(
        V2ConversationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var text = request.Text.ToLowerInvariant();
        var emptyInput = JsonSerializer.SerializeToElement(new { });
        var mentionsMail = text.Contains("gmail", StringComparison.Ordinal) ||
                           text.Contains("email", StringComparison.Ordinal) ||
                           text.Contains("mail", StringComparison.Ordinal);
        var explicitlyIncoming = text.Contains("incoming", StringComparison.Ordinal) ||
                                 text.Contains("inbox", StringComparison.Ordinal);
        var requestsLatest = text.Contains("latest", StringComparison.Ordinal) ||
                             text.Contains("last", StringComparison.Ordinal) ||
                             text.Contains("newest", StringComparison.Ordinal) ||
                             text.Contains("recent", StringComparison.Ordinal);
        var explicitlyOutgoing = text.Contains("send", StringComparison.Ordinal) ||
                                 text.Contains("sent", StringComparison.Ordinal) ||
                                 text.Contains("outgoing", StringComparison.Ordinal) ||
                                 text.Contains("draft", StringComparison.Ordinal) ||
                                 text.Contains("compose", StringComparison.Ordinal);
        var requestsLatestIncoming = explicitlyIncoming || (requestsLatest && !explicitlyOutgoing);
        if (mentionsMail && requestsLatestIncoming)
            return Task.FromResult<IReadOnlyList<V2ToolInvocation>>([
                new V2ToolInvocation(V2GmailTools.ReadLatest, emptyInput)
            ]);

        var mentionsSalesforce = text.Contains("salesforce", StringComparison.Ordinal) ||
                                 text.Contains("crm", StringComparison.Ordinal);
        var mentionsAccount = text.Contains("account", StringComparison.Ordinal) ||
                              text.Contains("customer", StringComparison.Ordinal) ||
                              text.Contains("organization", StringComparison.Ordinal);
        var requestsRead = text.Contains("get", StringComparison.Ordinal) ||
                           text.Contains("show", StringComparison.Ordinal) ||
                           text.Contains("read", StringComparison.Ordinal) ||
                           text.Contains("latest", StringComparison.Ordinal) ||
                           text.Contains("last", StringComparison.Ordinal) ||
                           text.Contains("recent", StringComparison.Ordinal);
        var requestsMutation = text.Contains("create", StringComparison.Ordinal) ||
                               text.Contains("update", StringComparison.Ordinal) ||
                               text.Contains("delete", StringComparison.Ordinal) ||
                               text.Contains("modify", StringComparison.Ordinal) ||
                               text.Contains("change", StringComparison.Ordinal);
        if (mentionsSalesforce && mentionsAccount && requestsRead && !requestsMutation)
            return Task.FromResult<IReadOnlyList<V2ToolInvocation>>([
                new V2ToolInvocation(V2SalesforceTools.ReadLatestAccount, emptyInput)
            ]);

        return Task.FromResult<IReadOnlyList<V2ToolInvocation>>([]);
    }
}

public interface IV2McpIntegrationToolGateway
{
    Task<V2GmailReadResult> ReadLatestIncomingAsync(string ownerScope, CancellationToken cancellationToken = default);

    Task<V2SalesforceReadResult> ReadLatestSalesforceAccountAsync(string ownerScope, CancellationToken cancellationToken = default);
}

public sealed class V2McpIntegrationToolGateway(IClusterClient cluster) : IV2McpIntegrationToolGateway
{
    public Task<V2GmailReadResult> ReadLatestIncomingAsync(
        string ownerScope,
        CancellationToken cancellationToken = default) =>
        cluster.GetGrain<IV2GmailReadToolGrain>(ownerScope).ReadLatestIncomingAsync(cancellationToken);

    public Task<V2SalesforceReadResult> ReadLatestSalesforceAccountAsync(
        string ownerScope,
        CancellationToken cancellationToken = default) =>
        cluster.GetGrain<IV2SalesforceReadToolGrain>(ownerScope).ReadLatestAccountAsync(cancellationToken);
}

public sealed class V2McpAuthorizedToolCatalog(IV2McpIntegrationToolGateway integrations) : IV2AuthorizedToolCatalog
{
    public async Task<V2ToolOutcome> InvokeAsync(
        V2RequestContext context,
        V2ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (invocation.Input.ValueKind != JsonValueKind.Object ||
            invocation.Input.EnumerateObject().Any())
            return new V2ToolOutcome(V2ToolOutcomeKind.Denied, SafeReason: "That tool request is not allowed.");
        if (string.Equals(invocation.ToolId, V2GmailTools.ReadLatest, StringComparison.Ordinal))
            return await InvokeGmailAsync(context, cancellationToken);
        if (string.Equals(invocation.ToolId, V2SalesforceTools.ReadLatestAccount, StringComparison.Ordinal))
            return await InvokeSalesforceAsync(context, cancellationToken);
        return new V2ToolOutcome(V2ToolOutcomeKind.Denied, SafeReason: "That tool request is not allowed.");
    }

    private async Task<V2ToolOutcome> InvokeGmailAsync(
        V2RequestContext context,
        CancellationToken cancellationToken)
    {
        if (context.Principal.Kind != PrincipalKind.User || !context.Grants.Contains("gmail.read"))
            return new V2ToolOutcome(
                V2ToolOutcomeKind.Denied,
                SafeReason: "You don’t have permission to read Gmail in this workspace.");

        var result = await integrations.ReadLatestIncomingAsync(V2RequestScope.Id(context), cancellationToken);
        return result.Status switch
        {
            V2GmailReadStatus.Success => new V2ToolOutcome(
                V2ToolOutcomeKind.Success,
                JsonSerializer.SerializeToElement(new { latestMessage = result.Content ?? string.Empty })),
            V2GmailReadStatus.NeedsAuth when IsAllowedGoogleAuthorizationUrl(result.ConnectionUrl) => new V2ToolOutcome(
                V2ToolOutcomeKind.NeedsAuth,
                SafeReason: result.SafeReason ?? "Connect your Google account to let INO read your Gmail.",
                Action: new V2ToolAction("openUrl", "Connect Google", result.ConnectionUrl!)),
            V2GmailReadStatus.NeedsAuth => new V2ToolOutcome(
                V2ToolOutcomeKind.PermanentFailure,
                SafeReason: "Gmail connection is unavailable right now."),
            V2GmailReadStatus.ConfigurationMissing => new V2ToolOutcome(
                V2ToolOutcomeKind.PermanentFailure,
                SafeReason: result.SafeReason ?? "Gmail application configuration is missing."),
            _ => new V2ToolOutcome(
                V2ToolOutcomeKind.RetryableFailure,
                SafeReason: result.SafeReason ?? "I couldn’t read Gmail right now. Please try again later.")
        };
    }

    private async Task<V2ToolOutcome> InvokeSalesforceAsync(
        V2RequestContext context,
        CancellationToken cancellationToken)
    {
        if (context.Principal.Kind != PrincipalKind.User || !context.Grants.Contains("salesforce.read"))
            return new V2ToolOutcome(
                V2ToolOutcomeKind.Denied,
                SafeReason: "You don’t have permission to read Salesforce in this workspace.");

        var result = await integrations.ReadLatestSalesforceAccountAsync(V2RequestScope.Id(context), cancellationToken);
        return result.Status switch
        {
            V2SalesforceReadStatus.Success => new V2ToolOutcome(
                V2ToolOutcomeKind.Success,
                JsonSerializer.SerializeToElement(new { latestAccount = result.Content ?? string.Empty })),
            V2SalesforceReadStatus.NeedsAuth when IsAllowedSalesforceAuthorizationUrl(result.ConnectionUrl) => new V2ToolOutcome(
                V2ToolOutcomeKind.NeedsAuth,
                SafeReason: result.SafeReason ?? "Connect your Salesforce account to let INO read Salesforce.",
                Action: new V2ToolAction("openUrl", "Connect Salesforce", result.ConnectionUrl!)),
            V2SalesforceReadStatus.NeedsAuth => new V2ToolOutcome(
                V2ToolOutcomeKind.PermanentFailure,
                SafeReason: "Salesforce connection is unavailable right now."),
            V2SalesforceReadStatus.ConfigurationMissing => new V2ToolOutcome(
                V2ToolOutcomeKind.PermanentFailure,
                SafeReason: result.SafeReason ?? "Salesforce application configuration is missing."),
            _ => new V2ToolOutcome(
                V2ToolOutcomeKind.RetryableFailure,
                SafeReason: result.SafeReason ?? "I couldn’t read Salesforce right now. Please try again later.")
        };
    }

    private static bool IsAllowedGoogleAuthorizationUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        string.Equals(uri.Host, "accounts.google.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedSalesforceAuthorizationUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        (string.Equals(uri.Host, "login.salesforce.com", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(uri.Host, "test.salesforce.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".my.salesforce.com", StringComparison.OrdinalIgnoreCase));
}

public sealed class V2McpResponseComposer : IV2ResponseSurfaceComposer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly Regex UnsafeAddress = new(
        @"\b[a-z][a-z0-9+.-]*://|\bwww\.|(?<![\p{L}\p{N}_/@.-])(?:[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?\.)+(?:[a-z]{2,63}|xn--[a-z0-9-]{2,59})(?::\d{2,5})?(?![\p{L}\p{N}_-])|" +
        @"(?<![\p{L}\p{N}.-])(?=[a-z0-9.-]*[a-z])(?:[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?):\d{2,5}(?!\d)|" +
        @"\b(?:\d{1,3}\.){3}\d{1,3}(?::\d+)?\b|" +
        @"(?<![\p{L}\p{N}:])(?:[0-9a-f]{0,4}:){2,7}[0-9a-f]{0,4}(?![\p{L}\p{N}:])|" +
        @"(?<!\\)\\\\[a-z0-9._$-]+(?:\\[^\s\\/:*?""<>|]+)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex UnsafeTerm = new(
        @"\b(?:idempotenc(?:y|e)|tenant|principal|grants?|tokens?|endpoints?|urls?|infrastructure|grpc|v2|secrets?|bearer)\b|" +
        @"\bfeed[\s_-]*metadata\b|\bsurface[\s_-]*(?:feed|revision)\b|\bfeed[\s_-]*sequence\b|\bwatchsurfacefeed\b|" +
        @"\b(?:operation|tenant|workspace|principal|binding)[\s_-]*(?:id|identifier)\b|\bapi[\s_-]*key\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    public Task<string> ComposeAsync(
        V2RequestContext context,
        V2ModelResponse response,
        IReadOnlyList<V2ToolOutcome> toolOutcomes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var blockingOutcome = toolOutcomes.FirstOrDefault(static outcome => outcome.Kind != V2ToolOutcomeKind.Success);
        if (blockingOutcome is not null)
            return Task.FromResult(blockingOutcome.SafeReason ?? "I couldn’t complete that request safely.");
        if (string.IsNullOrWhiteSpace(response.Text))
            throw new InvalidOperationException("The configured model returned no answer.");
        var text = response.Text.Trim();
        if (UnsafeAddress.IsMatch(text) || UnsafeTerm.IsMatch(text) || ContainsSensitiveContextValue(text, context))
            throw new InvalidOperationException("The configured model returned an answer that is unsafe to display.");
        return Task.FromResult(text);
    }

    private static bool ContainsSensitiveContextValue(string text, V2RequestContext context) =>
        ContainsScopeValue(text, "tenant", context.TenantId.Value) ||
        ContainsScopeValue(text, "workspace", context.WorkspaceId.Value) ||
        ContainsScopeValue(text, "principal", context.Principal.Value) ||
        context.Grants.Any(grant => ContainsDistinctIdentifier(text, grant));

    private static bool ContainsScopeValue(string text, string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Length >= 8 && ContainsDistinctIdentifier(text, value)) return true;
        return Regex.IsMatch(
            text,
            $@"(?<![\p{{L}}\p{{N}}]){label}(?:[\s_-]+(?:id|identifier))?" +
            $@"(?:(?:\s*[:=#]\s*|\s+(?:is|equals?|named)\s+)(?:['""`\(\[]\s*)?|\s+['""`\(\[]\s*)" +
            DistinctIdentifierPattern(value),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout);
    }

    private static bool ContainsDistinctIdentifier(string text, string value) =>
        !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(
            text,
            DistinctIdentifierPattern(value),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout);

    private static string DistinctIdentifierPattern(string value) =>
        $@"(?<![\p{{L}}\p{{N}}_/+%-])(?<![\p{{L}}\p{{N}}_][.:]){Regex.Escape(value)}(?![\p{{L}}\p{{N}}_/+%-]|[.:](?=[\p{{L}}\p{{N}}_]))";
}
