using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.Core.V2;
using DigitalBrain.Kernel.V2;
using Orleans;
using V2RequestContext = DigitalBrain.Core.V2.RequestContext;

namespace DigitalBrain.Mcp;

internal static class V2InoTelemetry
{
    public static readonly ActivitySource Source = new("DigitalBrain.Mcp");
}

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
        V2ToolAction? action = null,
        V2ToolGrounding? grounding = null,
        IReadOnlyList<V2ToolGrounding>? groundings = null)
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
            var hasAssistant = current.Turns.Any(turn =>
                string.Equals(turn.CommandId, commandId, StringComparison.Ordinal) &&
                string.Equals(turn.Role, "assistant", StringComparison.Ordinal));
            var candidate = current with
            {
                Turns = hasAssistant
                    ? current.Turns.Select(turn =>
                        string.Equals(turn.CommandId, commandId, StringComparison.Ordinal) &&
                        string.Equals(turn.Role, "assistant", StringComparison.Ordinal)
                            ? turn with { Text = safeResponse, State = V2InoConversationStates.Succeeded }
                            : turn).ToArray()
                    : current.Turns.Concat([
                        new V2InoConversationTurn(
                            commandId,
                            "assistant",
                            safeResponse,
                            V2InoConversationStates.Succeeded)
                    ]).ToArray()
            };
            candidate = PruneCompletedEntries(candidate, commandId);
            operation = candidate.Operations.Single(candidateOperation =>
                string.Equals(candidateOperation.CommandId, commandId, StringComparison.Ordinal));
            var next = ReplaceOperationAndUserTurn(candidate, operation with
            {
                State = V2InoConversationStates.Succeeded,
                SafeReason = null,
                Retryable = false,
                Action = action,
                Grounding = grounding is null
                    ? null
                    : new V2ToolGrounding(grounding.ToolId, grounding.Content.Clone()),
                Groundings = groundings?.Select(static value =>
                    new V2ToolGrounding(value.ToolId, value.Content.Clone())).ToArray(),
                UpdatedAt = DateTimeOffset.UtcNow
            }, V2InoConversationStates.Succeeded);
            next = FitAssistantResponse(next, commandId, safeResponse);
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
                var now = DateTimeOffset.UtcNow;
                var recovered = pair.Value with
                {
                    Revision = checked(pair.Value.Revision + 1),
                    Turns = pair.Value.Turns
                        .Where(turn => !active.Contains(turn.CommandId) || turn.Role != "assistant")
                        .Select(turn => active.Contains(turn.CommandId)
                            ? turn with { State = V2InoConversationStates.Failed }
                            : turn)
                        .ToArray(),
                    Operations = pair.Value.Operations.Select(operation => active.Contains(operation.CommandId)
                        ? operation with
                        {
                            State = V2InoConversationStates.Failed,
                            SafeReason = InterruptedReason,
                            Retryable = false,
                            Action = null,
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
        Operations = snapshot.Operations.Select(static operation => operation with
        {
            Grounding = operation.Grounding is null
                ? null
                : new V2ToolGrounding(operation.Grounding.ToolId, operation.Grounding.Content.Clone()),
            Groundings = operation.Groundings?.Select(static grounding =>
                new V2ToolGrounding(grounding.ToolId, grounding.Content.Clone())).ToArray()
        }).ToArray()
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
        using var activity = V2InoTelemetry.Source.StartActivity("ino.conversation.execute", ActivityKind.Internal);
        activity?.SetTag("db.ino.command_type", command.Type);
        if (!TryGetPrompt(command.Payload, out var prompt))
        {
            activity?.SetTag("db.ino.outcome", "invalid");
            return new V2CommandExecutionResult(WorkflowState.Failed, "ino-request-invalid");
        }

        var snapshot = conversations.Begin(command.Context, command.CommandId, prompt);
        var prior = snapshot.Operations.Single(operation =>
            string.Equals(operation.CommandId, command.CommandId, StringComparison.Ordinal));
        if (string.Equals(prior.State, V2InoConversationStates.Succeeded, StringComparison.Ordinal))
        {
            activity?.SetTag("db.ino.replay", true);
            activity?.SetTag("db.ino.outcome", "succeeded");
            surfaces.PublishInoConversation(command.Context, snapshot);
            return V2CommandExecutionResult.Success();
        }
        if (string.Equals(prior.State, V2InoConversationStates.Failed, StringComparison.Ordinal))
        {
            activity?.SetTag("db.ino.replay", true);
            activity?.SetTag("db.ino.outcome", "failed");
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
            snapshot = conversations.Complete(
                command.Context,
                command.CommandId,
                response.Text,
                response.Action,
                response.Grounding,
                response.Groundings);
            activity?.SetTag("db.ino.grounding_count", response.Groundings?.Count ?? (response.Grounding is null ? 0 : 1));
            activity?.SetTag("db.ino.outcome", "succeeded");
            surfaces.PublishInoConversation(command.Context, snapshot);
            return V2CommandExecutionResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            activity?.SetTag("db.ino.outcome", "cancelled");
            snapshot = conversations.Fail(command.Context, command.CommandId, SafeFailure, retryable: false);
            surfaces.PublishInoConversation(command.Context, snapshot);
            return new V2CommandExecutionResult(WorkflowState.Failed, SafeFailure);
        }
        catch (Exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "failed");
            activity?.SetTag("db.ino.outcome", "failed");
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

public interface IV2SemanticIntentResolver
{
    Task<V2SemanticIntentProposal> ResolveAsync(
        V2SemanticIntentRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class V2McpSemanticIntentResolver(IClusterClient cluster) : IV2SemanticIntentResolver
{
    public async Task<V2SemanticIntentProposal> ResolveAsync(
        V2SemanticIntentRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = V2InoTelemetry.Source.StartActivity("ino.intent.model", ActivityKind.Client);
        activity?.SetTag("db.ino.grounding_descriptor_count", request.Groundings.Count);
        var grainId = V2GrainIds.Conversation(
            new TenantId(request.TenantId),
            new WorkspaceId(request.WorkspaceId),
            request.ConversationId);
        var proposal = await cluster.GetGrain<IV2ConversationModelGrain>(grainId)
            .ResolveIntentAsync(request, cancellationToken).ConfigureAwait(false);
        activity?.SetTag("db.ino.provider", proposal.Provider.ToString());
        activity?.SetTag("db.ino.operation", proposal.Operation.ToString());
        return proposal;
    }
}

public sealed class V2McpIntegrationPlanner : IV2IntentCapabilityPlanner
{
    private const int MaximumDescriptors = 12;
    private const int MaximumSemanticText = 256;
    private static readonly JsonSerializerOptions SemanticJson = CreateSemanticJson();
    private readonly IV2SemanticIntentResolver _semanticIntents;
    private readonly IV2InoConversationStore? _conversations;

    public V2McpIntegrationPlanner(
        IV2SemanticIntentResolver semanticIntents,
        IV2InoConversationStore? conversations = null)
    {
        _semanticIntents = semanticIntents;
        _conversations = conversations;
    }

    public V2McpIntegrationPlanner() : this(new UnavailableSemanticIntentResolver()) { }

    public V2McpIntegrationPlanner(IV2InoConversationStore conversations)
        : this(new UnavailableSemanticIntentResolver(), conversations) { }

    public async Task<IReadOnlyList<V2ToolInvocation>> PlanAsync(
        V2ConversationRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = V2InoTelemetry.Source.StartActivity("ino.intent.plan", ActivityKind.Internal);
        cancellationToken.ThrowIfCancellationRequested();
        var descriptors = GroundingDescriptors(request);
        activity?.SetTag("db.ino.grounding_descriptor_count", descriptors.Count);
        var semanticRequest = new V2SemanticIntentRequest(
            request.Context.TenantId.Value,
            request.Context.WorkspaceId.Value,
            request.ConversationId,
            request.Text,
            descriptors);
        V2SemanticIntentProposal proposal;
        try
        {
            proposal = await _semanticIntents.ResolveAsync(semanticRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error, "intent-resolution-failed");
            activity?.SetTag("db.ino.outcome", "clarify");
            return [Clarification("I couldn’t safely determine which connected service you meant. Please name Gmail or Salesforce and the result you want.")];
        }

        if (!TryNormalize(proposal, out var normalized))
        {
            activity?.SetTag("db.ino.outcome", "invalid-proposal");
            return [Clarification("I need a little more detail before I can use a connected service safely.")];
        }
        activity?.SetTag("db.ino.provider", normalized.Provider.ToString());
        activity?.SetTag("db.ino.operation", normalized.Operation.ToString());
        if (normalized.Operation is V2SemanticOperation.QueryLanguage or V2SemanticOperation.Delete or
            V2SemanticOperation.MutationConfirm)
        {
            activity?.SetTag("db.ino.outcome", "denied");
            return [Clarification("I can use bounded typed reads and create a mutation preview, but I can’t run raw queries, deletes, or unbound confirmations.")];
        }
        if (normalized.Provider == V2SemanticProvider.None && normalized.Operation == V2SemanticOperation.Answer)
        {
            activity?.SetTag("db.ino.outcome", "general-answer");
            return [];
        }
        if (normalized.Provider == V2SemanticProvider.Ambiguous || normalized.Operation == V2SemanticOperation.Clarify)
        {
            activity?.SetTag("db.ino.outcome", "clarify");
            return [Clarification(normalized.Provider == V2SemanticProvider.Ambiguous
                ? "Do you mean Gmail or Salesforce?"
                : normalized.Clarification ?? "What should I look up, and in which connected service?")];
        }

        var toolId = ToolId(normalized);
        activity?.SetTag("db.ino.tool_id", toolId ?? "assistant.clarify");
        activity?.SetTag("db.ino.outcome", toolId is null ? "unsupported" : "planned");
        return toolId is null
            ? [Clarification("That connected-service operation isn’t available safely yet.")]
            : [new V2ToolInvocation(toolId, JsonSerializer.SerializeToElement(normalized, SemanticJson))];
    }

    private IReadOnlyList<V2GroundingDescriptor> GroundingDescriptors(V2ConversationRequest request)
    {
        if (_conversations is null) return [];
        var operations = _conversations.Read(request.Context).Operations;
        var result = new List<V2GroundingDescriptor>();
        var distance = 0;
        foreach (var operation in operations.Reverse())
        {
            if (V2InoConversationStates.IsActive(operation.State)) continue;
            distance++;
            if (!string.Equals(operation.State, V2InoConversationStates.Succeeded, StringComparison.Ordinal)) continue;
            var operationGroundings = operation.Groundings is { Count: > 0 }
                ? operation.Groundings
                : operation.Grounding is { } single
                    ? [single]
                    : [];
            foreach (var grounding in operationGroundings)
            {
                result.Add(new V2GroundingDescriptor(
                    Provider(grounding.ToolId),
                    grounding.ToolId,
                    ResultCount(grounding.Content),
                    HasContinuation(grounding.Content),
                    distance));
                if (result.Count == MaximumDescriptors) break;
            }
            if (result.Count == MaximumDescriptors) break;
        }
        return result;
    }

    private static string? ToolId(V2SemanticIntentProposal proposal) => proposal.Provider switch
    {
        V2SemanticProvider.Gmail => proposal.Operation switch
        {
            V2SemanticOperation.List or V2SemanticOperation.Refine or V2SemanticOperation.Previous => V2GmailTools.ReadMessages,
            V2SemanticOperation.Overview => V2GmailTools.ReadMailboxOverview,
            V2SemanticOperation.Threads => V2GmailTools.ReadThreads,
            V2SemanticOperation.Summarize => V2GmailTools.SummarizeThread,
            _ => null
        },
        V2SemanticProvider.Salesforce => proposal.Operation switch
        {
            V2SemanticOperation.Discover => V2SalesforceTools.DiscoverObjects,
            V2SemanticOperation.Search => V2SalesforceTools.SearchRecords,
            V2SemanticOperation.Aggregate => V2SalesforceTools.AggregateRecords,
            V2SemanticOperation.NextPage => V2SalesforceTools.ContinueRecords,
            V2SemanticOperation.MutationPreview => V2SalesforceTools.PreviewMutation,
            V2SemanticOperation.List or V2SemanticOperation.Refine or V2SemanticOperation.Related or
                V2SemanticOperation.Details or V2SemanticOperation.Previous => V2SalesforceTools.ReadRecords,
            _ => null
        },
        V2SemanticProvider.CrossProvider when proposal.Operation == V2SemanticOperation.Match &&
                                                 proposal.Reference == V2SemanticReference.LatestGmailSender =>
            V2CrossProviderTools.MatchSalesforceAccountToGmailSender,
        _ => null
    };

    private static bool TryNormalize(
        V2SemanticIntentProposal? proposal,
        out V2SemanticIntentProposal normalized)
    {
        normalized = default!;
        if (proposal is null || proposal.Limit is < 1 or > V2GmailTools.MaximumResultCount ||
            proposal.Ordinal is < 1 or > V2GmailTools.MaximumResultCount ||
            proposal.Filters is { Count: > 8 } || proposal.Sorts is { Count: > 8 } ||
            !ValidText(proposal.Entity, required: false) ||
            !ValidText(proposal.SearchText, required: false) ||
            !ValidText(proposal.Clarification, required: false) ||
            proposal.Filters?.Any(static filter =>
                !ValidText(filter.Field, required: true) || !ValidText(filter.Value, required: false)) == true ||
            proposal.Sorts?.Any(static sort => !ValidText(sort.Field, required: true)) == true ||
            (proposal.Aggregate is { } aggregate &&
             (!ValidText(aggregate.Field, required: false) || !ValidText(aggregate.GroupBy, required: false))))
            return false;

        normalized = proposal with
        {
            Entity = NormalizeText(proposal.Entity),
            SearchText = NormalizeText(proposal.SearchText),
            Clarification = NormalizeText(proposal.Clarification),
            Filters = proposal.Filters?.Select(static filter => filter with
            {
                Field = filter.Field.Trim(),
                Value = NormalizeText(filter.Value)
            }).ToArray(),
            Sorts = proposal.Sorts?.Select(static sort => sort with { Field = sort.Field.Trim() }).ToArray(),
            Aggregate = proposal.Aggregate is null
                ? null
                : proposal.Aggregate with
                {
                    Field = NormalizeText(proposal.Aggregate.Field),
                    GroupBy = NormalizeText(proposal.Aggregate.GroupBy)
                }
        };
        return true;
    }

    private static bool ValidText(string? value, bool required) =>
        value is null ? !required : value.Trim().Length is > 0 and <= MaximumSemanticText && !value.Any(char.IsControl);

    private static string? NormalizeText(string? value) => value?.Trim();

    private static V2ToolInvocation Clarification(string message) =>
        new(V2AssistantTools.Clarify, JsonSerializer.SerializeToElement(new { message }));

    private static string Provider(string toolId) => toolId.StartsWith("gmail.", StringComparison.Ordinal)
        ? "gmail"
        : toolId.StartsWith("salesforce.", StringComparison.Ordinal)
            ? "salesforce"
            : "crossProvider";

    private static int ResultCount(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object) return 0;
        if (content.TryGetProperty("resultCount", out var directCount) && directCount.TryGetInt32(out var count))
            return Math.Max(0, count);
        foreach (var property in content.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array) return property.Value.GetArrayLength();
            if (property.Value.ValueKind != JsonValueKind.Object) continue;
            if (property.Value.TryGetProperty("resultCount", out var nestedCount) && nestedCount.TryGetInt32(out count))
                return Math.Max(0, count);
            foreach (var arrayName in new[] { "resultMessageIds", "threadIds", "recordIds" })
                if (property.Value.TryGetProperty(arrayName, out var values) && values.ValueKind == JsonValueKind.Array)
                    return values.GetArrayLength();
        }
        return 0;
    }

    private static bool HasContinuation(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object) return false;
        if (content.TryGetProperty("hasMore", out var direct) && direct.ValueKind == JsonValueKind.True) return true;
        return content.EnumerateObject().Any(static property =>
            property.Value.ValueKind == JsonValueKind.Object &&
            property.Value.TryGetProperty("hasMore", out var nested) && nested.ValueKind == JsonValueKind.True);
    }

    private static JsonSerializerOptions CreateSemanticJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed class UnavailableSemanticIntentResolver : IV2SemanticIntentResolver
    {
        public Task<V2SemanticIntentProposal> ResolveAsync(
            V2SemanticIntentRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new V2SemanticIntentProposal(V2SemanticProvider.None, V2SemanticOperation.Answer));
    }
}

public interface IV2McpIntegrationToolGateway
{
    Task<V2GmailReadResult> ReadIncomingAtOffsetAsync(
        string ownerScope,
        V2GmailReadRequest request,
        CancellationToken cancellationToken = default);

    Task<V2SalesforceReadResult> ReadSalesforceAsync(
        string ownerScope,
        string toolId,
        CancellationToken cancellationToken = default);

    Task<V2GmailMessageListResult> ReadGmailMessagesAsync(
        string ownerScope,
        V2GmailMessageListRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new V2GmailMessageListResult(
            V2GmailReadStatus.Unavailable,
            [],
            new V2GmailResultCoverage(0, 0, 0, 0, 0, false, false)));

    Task<V2GmailMailboxOverviewResult> ReadGmailMailboxOverviewAsync(
        string ownerScope,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new V2GmailMailboxOverviewResult(V2GmailReadStatus.Unavailable));

    Task<V2GmailThreadListResult> ReadGmailThreadsAsync(
        string ownerScope,
        V2GmailThreadListRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new V2GmailThreadListResult(
            V2GmailReadStatus.Unavailable,
            [],
            new V2GmailResultCoverage(0, 0, 0, 0, 0, false, false)));

    Task<V2SalesforceReadResult> DiscoverSalesforceObjectsAsync(
        string ownerScope,
        V2SalesforceDiscoveryRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new V2SalesforceReadResult(V2SalesforceReadStatus.Unavailable));

    Task<V2SalesforceReadResult> ReadSalesforceRecordsAsync(
        string ownerScope,
        V2SalesforceRecordReadRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new V2SalesforceReadResult(V2SalesforceReadStatus.Unavailable));

    Task<V2SalesforceReadResult> SearchSalesforceRecordsAsync(
        string ownerScope,
        V2SalesforceSearchRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new V2SalesforceReadResult(V2SalesforceReadStatus.Unavailable));

    Task<V2SalesforceReadResult> AggregateSalesforceRecordsAsync(
        string ownerScope,
        V2SalesforceAggregateRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new V2SalesforceReadResult(V2SalesforceReadStatus.Unavailable));

    Task<V2SalesforceReadResult> ContinueSalesforceRecordsAsync(
        string ownerScope,
        V2SalesforceContinuationRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new V2SalesforceReadResult(V2SalesforceReadStatus.Unavailable));
}

public sealed class V2McpIntegrationToolGateway(IClusterClient cluster) : IV2McpIntegrationToolGateway
{
    public async Task<V2GmailReadResult> ReadIncomingAtOffsetAsync(
        string ownerScope,
        V2GmailReadRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = V2InoTelemetry.Source.StartActivity("ino.provider.gmail", ActivityKind.Client);
        activity?.SetTag("db.ino.tool_id", V2GmailTools.ReadIncomingAtOffset);
        var result = await cluster.GetGrain<IV2GmailReadToolGrain>(ownerScope)
            .ReadIncomingAtOffsetAsync(request, cancellationToken).ConfigureAwait(false);
        activity?.SetTag("db.ino.provider_outcome", result.Status.ToString());
        return result;
    }

    public async Task<V2SalesforceReadResult> ReadSalesforceAsync(
        string ownerScope,
        string toolId,
        CancellationToken cancellationToken = default)
    {
        using var activity = V2InoTelemetry.Source.StartActivity("ino.provider.salesforce", ActivityKind.Client);
        activity?.SetTag("db.ino.tool_id", toolId);
        var grain = cluster.GetGrain<IV2SalesforceReadToolGrain>(ownerScope);
        var result = await (toolId switch
        {
            V2SalesforceTools.ReadLatestAccount => grain.ReadLatestAccountAsync(cancellationToken),
            V2SalesforceTools.ReadCurrentProfile => grain.ReadCurrentProfileAsync(cancellationToken),
            V2SalesforceTools.ReadRecentAccounts => grain.ReadRecentAccountsAsync(cancellationToken),
            V2SalesforceTools.ReadRecentContacts => grain.ReadRecentContactsAsync(cancellationToken),
            V2SalesforceTools.ReadCrmSchema => grain.ReadCrmSchemaAsync(cancellationToken),
            _ => Task.FromResult(new V2SalesforceReadResult(V2SalesforceReadStatus.Unavailable))
        }).ConfigureAwait(false);
        activity?.SetTag("db.ino.provider_outcome", result.Status.ToString());
        return result;
    }

    public async Task<V2GmailMessageListResult> ReadGmailMessagesAsync(
        string ownerScope,
        V2GmailMessageListRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = V2InoTelemetry.Source.StartActivity("ino.provider.gmail", ActivityKind.Client);
        activity?.SetTag("db.ino.tool_id", V2GmailTools.ReadMessages);
        var result = await cluster.GetGrain<IV2GmailMetadataToolGrain>(ownerScope)
            .ReadMessagesAsync(request, cancellationToken).ConfigureAwait(false);
        activity?.SetTag("db.ino.provider_outcome", result.Status.ToString());
        activity?.SetTag("db.ino.result_count", result.Messages.Length);
        return result;
    }

    public async Task<V2GmailMailboxOverviewResult> ReadGmailMailboxOverviewAsync(
        string ownerScope,
        CancellationToken cancellationToken = default)
    {
        using var activity = V2InoTelemetry.Source.StartActivity("ino.provider.gmail", ActivityKind.Client);
        activity?.SetTag("db.ino.tool_id", V2GmailTools.ReadMailboxOverview);
        var result = await cluster.GetGrain<IV2GmailMetadataToolGrain>(ownerScope)
            .ReadMailboxOverviewAsync(cancellationToken).ConfigureAwait(false);
        activity?.SetTag("db.ino.provider_outcome", result.Status.ToString());
        return result;
    }

    public async Task<V2GmailThreadListResult> ReadGmailThreadsAsync(
        string ownerScope,
        V2GmailThreadListRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = V2InoTelemetry.Source.StartActivity("ino.provider.gmail", ActivityKind.Client);
        activity?.SetTag("db.ino.tool_id", V2GmailTools.ReadThreads);
        var result = await cluster.GetGrain<IV2GmailMetadataToolGrain>(ownerScope)
            .ReadThreadsAsync(request, cancellationToken).ConfigureAwait(false);
        activity?.SetTag("db.ino.provider_outcome", result.Status.ToString());
        activity?.SetTag("db.ino.result_count", result.Threads.Length);
        return result;
    }

    public Task<V2SalesforceReadResult> DiscoverSalesforceObjectsAsync(
        string ownerScope,
        V2SalesforceDiscoveryRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeSalesforceAsync(ownerScope, V2SalesforceTools.DiscoverObjects,
            grain => grain.DiscoverObjectsAsync(request, cancellationToken));

    public Task<V2SalesforceReadResult> ReadSalesforceRecordsAsync(
        string ownerScope,
        V2SalesforceRecordReadRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeSalesforceAsync(ownerScope, V2SalesforceTools.ReadRecords,
            grain => grain.ReadRecordsAsync(request, cancellationToken));

    public Task<V2SalesforceReadResult> SearchSalesforceRecordsAsync(
        string ownerScope,
        V2SalesforceSearchRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeSalesforceAsync(ownerScope, V2SalesforceTools.SearchRecords,
            grain => grain.SearchRecordsAsync(request, cancellationToken));

    public Task<V2SalesforceReadResult> AggregateSalesforceRecordsAsync(
        string ownerScope,
        V2SalesforceAggregateRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeSalesforceAsync(ownerScope, V2SalesforceTools.AggregateRecords,
            grain => grain.AggregateRecordsAsync(request, cancellationToken));

    public Task<V2SalesforceReadResult> ContinueSalesforceRecordsAsync(
        string ownerScope,
        V2SalesforceContinuationRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeSalesforceAsync(ownerScope, V2SalesforceTools.ContinueRecords,
            grain => grain.ContinueRecordsAsync(request, cancellationToken));

    private async Task<V2SalesforceReadResult> InvokeSalesforceAsync(
        string ownerScope,
        string toolId,
        Func<IV2SalesforceReadToolGrain, Task<V2SalesforceReadResult>> invoke)
    {
        using var activity = V2InoTelemetry.Source.StartActivity("ino.provider.salesforce", ActivityKind.Client);
        activity?.SetTag("db.ino.tool_id", toolId);
        var result = await invoke(cluster.GetGrain<IV2SalesforceReadToolGrain>(ownerScope)).ConfigureAwait(false);
        activity?.SetTag("db.ino.provider_outcome", result.Status.ToString());
        activity?.SetTag("db.ino.result_count", result.ReturnedCount);
        return result;
    }
}

public sealed class V2McpAuthorizedToolCatalog : IV2AuthorizedToolCatalog
{
    private const int MaximumSemanticText = 256;
    private static readonly JsonSerializerOptions SemanticJson = CreateSemanticJson();
    private readonly IV2McpIntegrationToolGateway _integrations;
    private readonly IV2InoConversationStore? _conversations;

    public V2McpAuthorizedToolCatalog(
        IV2McpIntegrationToolGateway integrations,
        IV2InoConversationStore? conversations = null)
    {
        _integrations = integrations;
        _conversations = conversations;
    }

    public async Task<V2ToolOutcome> InvokeAsync(
        V2RequestContext context,
        V2ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        using var activity = V2InoTelemetry.Source.StartActivity("ino.tool.invoke", ActivityKind.Internal);
        activity?.SetTag("db.ino.tool_id", invocation.ToolId);
        var outcome = await InvokeCoreAsync(context, invocation, cancellationToken).ConfigureAwait(false);
        activity?.SetTag("db.ino.tool_outcome", outcome.Kind.ToString());
        activity?.SetTag("db.ino.has_grounding", outcome.Kind == V2ToolOutcomeKind.Success && outcome.Content is not null);
        if (outcome.Kind is V2ToolOutcomeKind.RetryableFailure or V2ToolOutcomeKind.PermanentFailure or
            V2ToolOutcomeKind.OutcomeUnknown)
            activity?.SetStatus(ActivityStatusCode.Error, outcome.Kind.ToString());
        return outcome;
    }

    private async Task<V2ToolOutcome> InvokeCoreAsync(
        V2RequestContext context,
        V2ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.Equals(invocation.ToolId, V2AssistantTools.Clarify, StringComparison.Ordinal))
        {
            if (invocation.Input.ValueKind != JsonValueKind.Object ||
                invocation.Input.EnumerateObject().Count() != 1 ||
                !invocation.Input.TryGetProperty("message", out var messageElement) ||
                messageElement.ValueKind != JsonValueKind.String ||
                messageElement.GetString() is not { } message ||
                message.Length is < 1 or > 256 || message.Any(char.IsControl))
                return new V2ToolOutcome(V2ToolOutcomeKind.Denied, SafeReason: "That clarification is not safe to display.");
            return new V2ToolOutcome(V2ToolOutcomeKind.PermanentFailure, SafeReason: message);
        }
        if (IsSemanticTool(invocation.ToolId))
        {
            if (!TryParseSemanticIntent(invocation.ToolId, invocation.Input, out var proposal))
                return new V2ToolOutcome(
                    V2ToolOutcomeKind.Denied,
                    SafeReason: "That connected-service request is not a valid typed operation.");
            if (invocation.ToolId.StartsWith("gmail.", StringComparison.Ordinal))
                return await InvokeTypedGmailAsync(context, invocation.ToolId, proposal, cancellationToken);
            if (invocation.ToolId.StartsWith("salesforce.", StringComparison.Ordinal))
                return await InvokeTypedSalesforceAsync(context, invocation.ToolId, proposal, cancellationToken);
            return await InvokeCrossProviderAsync(context, proposal, cancellationToken);
        }
        if (string.Equals(invocation.ToolId, V2GmailTools.SummarizeIncoming, StringComparison.Ordinal))
        {
            if (invocation.Input.ValueKind != JsonValueKind.Object || invocation.Input.EnumerateObject().Any())
                return new V2ToolOutcome(V2ToolOutcomeKind.Denied, SafeReason: "That Gmail request is not allowed.");
            if (context.Principal.Kind != PrincipalKind.User || !context.Grants.Contains("gmail.read"))
                return new V2ToolOutcome(
                    V2ToolOutcomeKind.Denied,
                    SafeReason: "You don’t have permission to read Gmail in this workspace.");
            return new V2ToolOutcome(
                V2ToolOutcomeKind.PermanentFailure,
                SafeReason: "I can’t summarize email content because Gmail access is limited to sender metadata. I won’t read bodies or snippets.");
        }
        if (string.Equals(invocation.ToolId, V2GmailTools.ReadIncomingAtOffset, StringComparison.Ordinal))
        {
            if (!TryParseGmailRequest(invocation.Input, out var gmailRequest))
                return new V2ToolOutcome(V2ToolOutcomeKind.Denied, SafeReason: "That Gmail position cannot be read safely.");
            if (gmailRequest.RequiresAnchor &&
                (string.IsNullOrWhiteSpace(gmailRequest.AnchorMessageId) || gmailRequest.AnchorInternalDate is null ||
                 gmailRequest.TraversalDepth > V2GmailTools.MaximumOffset))
                return new V2ToolOutcome(
                    V2ToolOutcomeKind.PermanentFailure,
                    SafeReason: "I can’t safely resolve that previous email from the immediately preceding turn. Ask for the latest incoming email to start again.");
            return await InvokeGmailAsync(context, gmailRequest, cancellationToken);
        }
        if (invocation.Input.ValueKind != JsonValueKind.Object || invocation.Input.EnumerateObject().Any())
            return new V2ToolOutcome(V2ToolOutcomeKind.Denied, SafeReason: "That tool request is not allowed.");
        return new V2ToolOutcome(V2ToolOutcomeKind.Denied, SafeReason: "That tool request is not allowed.");
    }

    private async Task<V2ToolOutcome> InvokeGmailAsync(
        V2RequestContext context,
        V2GmailReadRequest request,
        CancellationToken cancellationToken)
    {
        if (context.Principal.Kind != PrincipalKind.User || !context.Grants.Contains("gmail.read"))
            return new V2ToolOutcome(
                V2ToolOutcomeKind.Denied,
                SafeReason: "You don’t have permission to read Gmail in this workspace.");

        var result = await _integrations.ReadIncomingAtOffsetAsync(V2RequestScope.Id(context), request, cancellationToken);
        return result.Status switch
        {
            V2GmailReadStatus.Success => new V2ToolOutcome(
                V2ToolOutcomeKind.Success,
                JsonSerializer.SerializeToElement(new
                {
                    incomingMessage = new
                    {
                        status = GmailMailboxStatus(result.MailboxState),
                        sender = result.Sender,
                        senderAddress = result.SenderAddress,
                        messageId = result.MessageId,
                        internalDate = result.InternalDate,
                        traversalDepth = result.TraversalDepth,
                        anchoredPrevious = result.AnchoredPrevious
                    }
                }),
                GroundingContent: JsonSerializer.SerializeToElement(new
                {
                    incomingMessage = new
                    {
                        senderAddress = result.SenderAddress,
                        messageId = result.MessageId,
                        internalDate = result.InternalDate,
                        traversalDepth = result.TraversalDepth,
                        anchoredPrevious = result.AnchoredPrevious
                    }
                })),
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

    private async Task<V2ToolOutcome> InvokeTypedGmailAsync(
        V2RequestContext context,
        string toolId,
        V2SemanticIntentProposal proposal,
        CancellationToken cancellationToken)
    {
        if (context.Principal.Kind != PrincipalKind.User || !context.Grants.Contains("gmail.read"))
            return new V2ToolOutcome(
                V2ToolOutcomeKind.Denied,
                SafeReason: "You don’t have permission to read Gmail in this workspace.");

        if (string.Equals(toolId, V2GmailTools.SummarizeThread, StringComparison.Ordinal))
        {
            if (!context.Grants.Contains("gmail.read.content"))
                return new V2ToolOutcome(
                    V2ToolOutcomeKind.Denied,
                    SafeReason: "Email content access is separate from Gmail metadata access. Grant gmail.read.content before asking for a summary.");
            return new V2ToolOutcome(
                V2ToolOutcomeKind.PermanentFailure,
                SafeReason: "Thread summaries are unavailable because this Gmail connection is metadata-only. No message body or snippet was read.");
        }

        var ownerScope = V2RequestScope.Id(context);
        if (string.Equals(toolId, V2GmailTools.ReadMailboxOverview, StringComparison.Ordinal))
        {
            var overview = await _integrations.ReadGmailMailboxOverviewAsync(ownerScope, cancellationToken);
            if (overview.Status != V2GmailReadStatus.Success)
                return GmailFailure(overview.Status, overview.SafeReason, overview.ConnectionUrl);
            return new V2ToolOutcome(
                V2ToolOutcomeKind.Success,
                JsonSerializer.SerializeToElement(new
                {
                    gmailMailboxOverview = new
                    {
                        overview.InboxMessages,
                        overview.UnreadInboxMessages,
                        overview.InboxThreads,
                        overview.UnreadInboxThreads
                    }
                }, SemanticJson));
        }

        if (!TryCompileGmailRequest(context, proposal, out var selection, out var offset, out var safeReason))
            return new V2ToolOutcome(V2ToolOutcomeKind.PermanentFailure, SafeReason: safeReason);

        if (string.Equals(toolId, V2GmailTools.ReadMessages, StringComparison.Ordinal))
        {
            var requestLimit = proposal.Ordinal is not null &&
                               proposal.Reference == V2SemanticReference.LatestProviderResult
                ? 1
                : proposal.Limit;
            var request = new V2GmailMessageListRequest(selection, offset, requestLimit);
            var result = await _integrations.ReadGmailMessagesAsync(ownerScope, request, cancellationToken);
            if (result.Status != V2GmailReadStatus.Success)
                return GmailFailure(result.Status, result.SafeReason, result.ConnectionUrl);
            var stableIds = (selection.PinnedMessageIds ?? result.StableCandidateMessageIds ??
                             result.Messages.Select(static message => message.MessageId).ToArray())
                .Where(ValidProviderIdentifier)
                .Distinct(StringComparer.Ordinal)
                .Take(V2GmailTools.MaximumCandidateCount)
                .ToArray();
            var consumedCandidates = selection.PinnedMessageIds is null
                ? result.Messages.Length
                : Math.Min(request.Limit, Math.Max(0, stableIds.Length - offset));
            var nextOffset = checked(offset + consumedCandidates);
            var hasMore = nextOffset < stableIds.Length;
            var stableSelection = selection with { PinnedMessageIds = stableIds.Length == 0 ? null : stableIds };
            var display = JsonSerializer.SerializeToElement(new
            {
                gmailMessages = new
                {
                    messages = result.Messages,
                    coverage = result.Coverage,
                    hasMore
                }
            }, SemanticJson);
            var grounding = JsonSerializer.SerializeToElement(new
            {
                gmailMessages = new
                {
                    resultMessageIds = result.Messages.Select(static message => message.MessageId).ToArray(),
                    senderAddresses = result.Messages.Select(static message => message.FromAddress)
                        .Where(static value => value is not null).ToArray(),
                    selection = stableSelection,
                    nextOffset,
                    hasMore
                }
            }, SemanticJson);
            return new V2ToolOutcome(
                V2ToolOutcomeKind.Success,
                display,
                GroundingContent: grounding);
        }

        var threadRequest = new V2GmailThreadListRequest(selection, offset, proposal.Limit);
        var threads = await _integrations.ReadGmailThreadsAsync(ownerScope, threadRequest, cancellationToken);
        if (threads.Status != V2GmailReadStatus.Success)
            return GmailFailure(threads.Status, threads.SafeReason, threads.ConnectionUrl);
        var stableThreadCandidateIds = (threads.StableCandidateMessageIds ?? threads.Threads
                .SelectMany(static thread => thread.Messages)
                .Select(static message => message.MessageId).ToArray())
            .Where(ValidProviderIdentifier)
            .Distinct(StringComparer.Ordinal)
            .Take(V2GmailTools.MaximumCandidateCount)
            .ToArray();
        var nextThreadOffset = checked(offset + threads.Threads.Length);
        var stableThreadIds = (threads.StableCandidateThreadIds ?? threads.Threads
                .Select(static thread => thread.ThreadId).ToArray())
            .Where(ValidProviderIdentifier)
            .Distinct(StringComparer.Ordinal)
            .Take(V2GmailTools.MaximumCandidateCount)
            .ToArray();
        var hasMoreThreads = nextThreadOffset < stableThreadIds.Length;
        var stableThreadSelection = selection with
        {
            PinnedMessageIds = stableThreadCandidateIds.Length == 0 ? null : stableThreadCandidateIds
        };
        var threadDisplay = JsonSerializer.SerializeToElement(new
        {
            gmailThreads = new
            {
                threads = threads.Threads,
                coverage = threads.Coverage,
                hasMore = hasMoreThreads
            }
        }, SemanticJson);
        var threadGrounding = JsonSerializer.SerializeToElement(new
        {
            gmailThreads = new
            {
                resultMessageIds = threads.Threads.SelectMany(static thread => thread.Messages)
                    .Select(static message => message.MessageId).ToArray(),
                threadIds = threads.Threads.Select(static thread => thread.ThreadId).ToArray(),
                stableThreadIds,
                senderAddresses = threads.Threads.SelectMany(static thread => thread.Messages)
                    .Select(static message => message.FromAddress)
                    .Where(static value => value is not null).ToArray(),
                selection = stableThreadSelection,
                nextOffset = nextThreadOffset,
                hasMore = hasMoreThreads
            }
        }, SemanticJson);
        return new V2ToolOutcome(
            V2ToolOutcomeKind.Success,
            threadDisplay,
            GroundingContent: threadGrounding);
    }

    private async Task<V2ToolOutcome> InvokeTypedSalesforceAsync(
        V2RequestContext context,
        string toolId,
        V2SemanticIntentProposal proposal,
        CancellationToken cancellationToken)
    {
        if (context.Principal.Kind != PrincipalKind.User || !context.Grants.Contains("salesforce.read"))
            return new V2ToolOutcome(
                V2ToolOutcomeKind.Denied,
                SafeReason: "You don’t have permission to read Salesforce in this workspace.");

        if (string.Equals(toolId, V2SalesforceTools.PreviewMutation, StringComparison.Ordinal))
        {
            if (!context.Grants.Contains("salesforce.mutation.preview"))
                return new V2ToolOutcome(
                    V2ToolOutcomeKind.Denied,
                    SafeReason: "A Salesforce mutation request preview requires the separate salesforce.mutation.preview grant. No record was changed.");
            return PreviewSalesforceMutation(proposal);
        }

        var ownerScope = V2RequestScope.Id(context);
        V2SalesforceReadResult result;
        string resultField;
        switch (toolId)
        {
            case V2SalesforceTools.DiscoverObjects:
                result = await _integrations.DiscoverSalesforceObjectsAsync(
                    ownerScope,
                    new V2SalesforceDiscoveryRequest(Math.Min(50, Math.Max(1, proposal.Limit))),
                    cancellationToken);
                resultField = "salesforceObjects";
                break;
            case V2SalesforceTools.SearchRecords:
                if (string.IsNullOrWhiteSpace(proposal.SearchText))
                    return InvalidTypedRequest("Tell me what to search for in Salesforce.");
                var searchEntities = IsAllAccessible(proposal.Entity)
                    ? null
                    : new[] { new V2SalesforceSemanticEntity(proposal.Entity!) };
                result = await _integrations.SearchSalesforceRecordsAsync(
                    ownerScope,
                    new V2SalesforceSearchRequest(proposal.SearchText, searchEntities, proposal.Limit),
                    cancellationToken);
                resultField = "salesforceSearch";
                break;
            case V2SalesforceTools.AggregateRecords:
                if (!TryCompileSalesforceAggregate(proposal, out var aggregate, out var aggregateReason))
                    return InvalidTypedRequest(aggregateReason);
                result = await _integrations.AggregateSalesforceRecordsAsync(ownerScope, aggregate, cancellationToken);
                resultField = "salesforceAggregate";
                break;
            case V2SalesforceTools.ContinueRecords:
                if (!TryGetSalesforceContinuation(context, out var continuation))
                    return InvalidTypedRequest("There is no stable Salesforce continuation to follow. Run the bounded read again.");
                result = await _integrations.ContinueSalesforceRecordsAsync(
                    ownerScope,
                    new V2SalesforceContinuationRequest(continuation),
                    cancellationToken);
                resultField = "salesforceRecords";
                break;
            default:
                if (!TryCompileSalesforceRead(context, proposal, out var read, out var readReason))
                    return InvalidTypedRequest(readReason);
                result = await _integrations.ReadSalesforceRecordsAsync(ownerScope, read, cancellationToken);
                resultField = "salesforceRecords";
                break;
        }

        return SalesforceOutcome(result, resultField, proposal.Entity ?? TryGetLatestSalesforceEntity(context));
    }

    private async Task<V2ToolOutcome> InvokeCrossProviderAsync(
        V2RequestContext context,
        V2SemanticIntentProposal proposal,
        CancellationToken cancellationToken)
    {
        if (context.Principal.Kind != PrincipalKind.User ||
            !context.Grants.Contains("gmail.read") ||
            !context.Grants.Contains("salesforce.read"))
            return new V2ToolOutcome(
                V2ToolOutcomeKind.Denied,
                SafeReason: "Matching Gmail to Salesforce requires both gmail.read and salesforce.read in this workspace.");

        var senderAddress = TryGetLatestGmailSender(context);
        if (senderAddress is null)
        {
            var gmail = await _integrations.ReadGmailMessagesAsync(
                V2RequestScope.Id(context),
                new V2GmailMessageListRequest(new V2GmailMessageSelection(), Limit: 1),
                cancellationToken);
            if (gmail.Status != V2GmailReadStatus.Success)
                return GmailFailure(gmail.Status, gmail.SafeReason, gmail.ConnectionUrl);
            senderAddress = gmail.Messages.FirstOrDefault()?.FromAddress;
        }
        if (!ValidProviderIdentifier(senderAddress) || !senderAddress!.Contains('@', StringComparison.Ordinal))
            return InvalidTypedRequest("The latest Gmail result has no usable sender address to match.");

        var salesforce = await _integrations.SearchSalesforceRecordsAsync(
            V2RequestScope.Id(context),
            new V2SalesforceSearchRequest(
                senderAddress,
                [new V2SalesforceSemanticEntity(proposal.Entity ?? "account")],
                Math.Min(3, proposal.Limit)),
            cancellationToken);
        if (salesforce.Status == V2SalesforceReadStatus.Success && salesforce.ReturnedCount > 1)
            return InvalidTypedRequest("More than one Salesforce account matched that sender. Please add an account name or domain.");
        return SalesforceOutcome(salesforce, "salesforceSearch", proposal.Entity ?? "account", senderAddress);
    }

    private static V2ToolOutcome PreviewSalesforceMutation(V2SemanticIntentProposal proposal)
    {
        var changes = proposal.Filters?.Where(static filter => filter.Operator == V2SemanticFilterOperator.Set).ToArray() ?? [];
        if (!ValidSemanticText(proposal.Entity, required: true) ||
            !ValidSemanticText(proposal.SearchText, required: true) ||
            changes.Length is < 1 or > 8)
            return InvalidTypedRequest("A mutation preview needs one bounded record match and at least one typed field change.");
        return new V2ToolOutcome(
            V2ToolOutcomeKind.Success,
            JsonSerializer.SerializeToElement(new
            {
                salesforceMutationPreview = new
                {
                    entity = proposal.Entity,
                    recordMatch = proposal.SearchText,
                    changes = changes.Select(static change => new { field = change.Field, value = change.Value }).ToArray(),
                    status = "previewOnly",
                    note = "This request has not been schema-verified and no Salesforce record was changed. A separately authorized, journaled confirmation operation is required."
                }
            }, SemanticJson));
    }

    private bool TryCompileGmailRequest(
        V2RequestContext context,
        V2SemanticIntentProposal proposal,
        out V2GmailMessageSelection selection,
        out int offset,
        out string safeReason)
    {
        selection = GmailSelectionForEntity(proposal.Entity);
        offset = Math.Max(0, (proposal.Ordinal ?? 1) - 1);
        safeReason = "That Gmail selection could not be compiled safely.";

        if (proposal.Reference != V2SemanticReference.None)
        {
            var grounding = LatestGrounding(context, "gmail.");
            if (grounding is null)
            {
                safeReason = "There is no grounded Gmail result to refine. Run a Gmail read first.";
                return false;
            }

            if (proposal.Operation == V2SemanticOperation.Previous)
            {
                if (!TryGetGmailSelection(grounding.Content, out selection) ||
                    !TryGetInt32(grounding.Content, "nextOffset", out var nextOffset) ||
                    !TryGetBoolean(grounding.Content, "hasMore", out var hasMore) || !hasMore)
                {
                    safeReason = "The prior bounded Gmail result has no stable next item.";
                    return false;
                }
                offset = checked(nextOffset + Math.Max(0, (proposal.Ordinal ?? 1) - 1));
            }
            else if (proposal.Reference == V2SemanticReference.LatestProviderResult)
            {
                if (TryGetGmailSelection(grounding.Content, out var priorSelection))
                    selection = priorSelection;
                var pinnedIds = GmailMessageIds(grounding.Content).Take(V2GmailTools.MaximumResultCount).ToArray();
                if (pinnedIds.Length == 0)
                {
                    safeReason = "The prior Gmail result has no stable message identifiers to refine.";
                    return false;
                }
                selection = selection with { PinnedMessageIds = pinnedIds };
            }
            else if (proposal.Reference is V2SemanticReference.SameSender or V2SemanticReference.LatestGmailSender)
            {
                var sender = GmailSender(grounding.Content);
                if (!ValidProviderIdentifier(sender) || !sender!.Contains('@', StringComparison.Ordinal))
                {
                    safeReason = "The prior Gmail result has no stable sender address to reuse.";
                    return false;
                }
                selection = selection with { SenderAddress = sender, PinnedMessageIds = null };
                offset = 0;
            }
        }

        foreach (var filter in proposal.Filters ?? [])
        {
            var field = NormalizeSemanticName(filter.Field);
            var value = filter.Value?.Trim();
            switch (field)
            {
                case "readstate":
                case "read":
                    selection = value?.Equals("unread", StringComparison.OrdinalIgnoreCase) == true
                        ? selection with { ReadState = V2GmailMessageReadState.Unread }
                        : value?.Equals("read", StringComparison.OrdinalIgnoreCase) == true
                            ? selection with { ReadState = V2GmailMessageReadState.Read }
                            : selection;
                    if (selection.ReadState == V2GmailMessageReadState.Any) return false;
                    break;
                case "attachment":
                case "attachments":
                    selection = value is not null &&
                                (value.Equals("present", StringComparison.OrdinalIgnoreCase) ||
                                 value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                                 value.Equals("yes", StringComparison.OrdinalIgnoreCase))
                        ? selection with { AttachmentFilter = V2GmailAttachmentFilter.HasAttachments }
                        : value is not null &&
                          (value.Equals("absent", StringComparison.OrdinalIgnoreCase) ||
                           value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                           value.Equals("no", StringComparison.OrdinalIgnoreCase))
                            ? selection with { AttachmentFilter = V2GmailAttachmentFilter.NoAttachments }
                            : selection;
                    if (selection.AttachmentFilter == V2GmailAttachmentFilter.Any) return false;
                    break;
                case "sender":
                case "from":
                    if (!ValidProviderIdentifier(value)) return false;
                    selection = selection with { SenderAddress = value };
                    break;
                case "recipient":
                case "to":
                    if (!ValidProviderIdentifier(value)) return false;
                    selection = selection with { RecipientAddress = value };
                    break;
                case "subject":
                    if (!ValidProviderIdentifier(value)) return false;
                    selection = selection with { SubjectContains = value };
                    break;
                default:
                    return false;
            }
        }

        if (proposal.TimeRange != V2SemanticTimeRange.None)
        {
            if (!TryGetTimeRange(proposal.TimeRange, out var from, out var until)) return false;
            selection = selection with
            {
                ReceivedAfterInclusive = from.ToUnixTimeMilliseconds(),
                ReceivedBeforeExclusive = until.ToUnixTimeMilliseconds()
            };
        }

        return offset < V2GmailTools.MaximumCandidateCount &&
               (selection.PinnedMessageIds is null || offset < selection.PinnedMessageIds.Length) &&
               selection.MaxPages is >= 1 and <= V2GmailTools.MaximumPageCount &&
               selection.MaxCandidates is >= 1 and <= V2GmailTools.MaximumCandidateCount;
    }

    private static V2GmailMessageSelection GmailSelectionForEntity(string? entity) =>
        NormalizeSemanticName(entity ?? string.Empty) switch
        {
            "inbox" => new V2GmailMessageSelection(V2GmailMailboxScope.Inbox),
            "sent" or "sentmail" => new V2GmailMessageSelection(V2GmailMailboxScope.Sent),
            "draft" or "drafts" => new V2GmailMessageSelection(V2GmailMailboxScope.Drafts),
            "all" or "allmail" => new V2GmailMessageSelection(V2GmailMailboxScope.All),
            _ => new V2GmailMessageSelection(V2GmailMailboxScope.Incoming)
        };

    private static V2ToolOutcome GmailFailure(V2GmailReadStatus status, string? safeReason, string? connectionUrl) => status switch
    {
        V2GmailReadStatus.NeedsAuth when IsAllowedGoogleAuthorizationUrl(connectionUrl) => new V2ToolOutcome(
            V2ToolOutcomeKind.NeedsAuth,
            SafeReason: SafeProviderReason(safeReason, "Connect your Google account to let INO read Gmail metadata."),
            Action: new V2ToolAction("openUrl", "Connect Google", connectionUrl!)),
        V2GmailReadStatus.NeedsAuth => new V2ToolOutcome(
            V2ToolOutcomeKind.PermanentFailure,
            SafeReason: "Gmail connection is unavailable right now."),
        V2GmailReadStatus.ConfigurationMissing => new V2ToolOutcome(
            V2ToolOutcomeKind.PermanentFailure,
            SafeReason: SafeProviderReason(safeReason, "Gmail application configuration is missing.")),
        V2GmailReadStatus.CapabilityUnavailable => new V2ToolOutcome(
            V2ToolOutcomeKind.PermanentFailure,
            SafeReason: SafeProviderReason(safeReason, "That Gmail metadata capability is unavailable. No body or snippet was read.")),
        _ => new V2ToolOutcome(
            V2ToolOutcomeKind.RetryableFailure,
            SafeReason: "I couldn’t read Gmail metadata right now. Please try again later.")
    };

    private bool TryCompileSalesforceRead(
        V2RequestContext context,
        V2SemanticIntentProposal proposal,
        out V2SalesforceRecordReadRequest request,
        out string safeReason)
    {
        request = default!;
        safeReason = "That Salesforce read could not be compiled safely.";
        if (!ValidSemanticText(proposal.Entity, required: true))
        {
            safeReason = "Name the Salesforce record type you want to read.";
            return false;
        }
        if (!TryCompileSalesforceFilters(proposal, out var filters)) return false;
        var kind = proposal.Operation switch
        {
            V2SemanticOperation.Details => V2SalesforceRecordReadKind.Details,
            V2SemanticOperation.Related => V2SalesforceRecordReadKind.Related,
            _ => V2SalesforceRecordReadKind.List
        };
        V2SalesforceResolvedRecord? record = null;
        V2SalesforceResolvedRecord? relatedTo = null;
        if (proposal.Reference is (V2SemanticReference.LatestProviderResult or V2SemanticReference.SameAccount) &&
            (kind is V2SalesforceRecordReadKind.Details or V2SalesforceRecordReadKind.Related ||
             proposal.Operation == V2SemanticOperation.Refine))
        {
            if (!TryGetSalesforceRecord(context, proposal.Ordinal, out var resolvedRecord, out var resultCount))
            {
                safeReason = resultCount > 1 && proposal.Ordinal is null
                    ? proposal.Operation == V2SemanticOperation.Refine
                        ? "That refinement needs one stable Salesforce record. Narrow the prior result first."
                        : "The prior Salesforce result contains multiple records. Specify a supported ordinal before asking for details or related records."
                    : proposal.Ordinal is not null && resultCount > 0
                        ? "That ordinal is not available in the grounded Salesforce result."
                        : "There is no grounded Salesforce record to reuse. Run a bounded Salesforce read first.";
                return false;
            }
            if (proposal.Operation == V2SemanticOperation.Refine && resultCount != 1)
            {
                safeReason = "That refinement needs one stable Salesforce record. Narrow the prior result first.";
                return false;
            }
            if (kind == V2SalesforceRecordReadKind.Related) relatedTo = resolvedRecord;
            else if (kind == V2SalesforceRecordReadKind.Details || proposal.Operation == V2SemanticOperation.Refine)
            {
                record = resolvedRecord;
                kind = V2SalesforceRecordReadKind.Details;
            }
        }
        request = new V2SalesforceRecordReadRequest(
            new V2SalesforceSemanticEntity(proposal.Entity!),
            kind,
            Filters: filters,
            Sorts: proposal.Sorts?.Select(static sort =>
                new V2SalesforceSort(new V2SalesforceSemanticField(sort.Field), sort.Direction)).ToArray(),
            Limit: proposal.Limit,
            Record: record,
            RelatedTo: relatedTo);
        return true;
    }

    private static bool TryCompileSalesforceAggregate(
        V2SemanticIntentProposal proposal,
        out V2SalesforceAggregateRequest request,
        out string safeReason)
    {
        request = default!;
        safeReason = "That Salesforce aggregate could not be compiled safely.";
        if (!ValidSemanticText(proposal.Entity, required: true) || proposal.Aggregate is null ||
            !TryCompileSalesforceFilters(proposal, out var filters))
            return false;
        request = new V2SalesforceAggregateRequest(
            new V2SalesforceSemanticEntity(proposal.Entity!),
            proposal.Aggregate.Function,
            proposal.Aggregate.Field is null ? null : new V2SalesforceSemanticField(proposal.Aggregate.Field),
            proposal.Aggregate.GroupBy is null ? null : new V2SalesforceSemanticField(proposal.Aggregate.GroupBy),
            filters,
            Math.Min(50, proposal.Limit));
        return true;
    }

    private static bool TryCompileSalesforceFilters(
        V2SemanticIntentProposal proposal,
        out IReadOnlyList<V2SalesforceFilter> filters)
    {
        var result = new List<V2SalesforceFilter>();
        foreach (var filter in proposal.Filters ?? [])
        {
            if (filter.Operator == V2SemanticFilterOperator.Set) { filters = []; return false; }
            if (NormalizeSemanticName(filter.Field) == "open")
            {
                result.Add(new V2SalesforceFilter(
                    new V2SalesforceSemanticField("Is Closed"),
                    V2SemanticFilterOperator.Equals,
                    "false"));
                continue;
            }
            result.Add(new V2SalesforceFilter(
                new V2SalesforceSemanticField(filter.Field),
                filter.Operator,
                filter.Value));
        }
        if (proposal.TimeRange != V2SemanticTimeRange.None)
        {
            if (!TryGetTimeRange(proposal.TimeRange, out var from, out var until)) { filters = []; return false; }
            result.Add(new V2SalesforceFilter(
                new V2SalesforceSemanticField("Close Date"),
                V2SemanticFilterOperator.GreaterThanOrEqual,
                from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
            result.Add(new V2SalesforceFilter(
                new V2SalesforceSemanticField("Close Date"),
                V2SemanticFilterOperator.LessThan,
                until.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        }
        filters = result;
        return true;
    }

    private V2ToolGrounding? LatestGrounding(V2RequestContext context, string toolPrefix)
    {
        if (_conversations is null) return null;
        foreach (var operation in _conversations.Read(context).Operations.Reverse())
        {
            if (!string.Equals(operation.State, V2InoConversationStates.Succeeded, StringComparison.Ordinal)) continue;
            var groundings = operation.Groundings is { Count: > 0 }
                ? operation.Groundings
                : operation.Grounding is { } grounding
                    ? [grounding]
                    : [];
            var match = groundings.FirstOrDefault(value => value.ToolId.StartsWith(toolPrefix, StringComparison.Ordinal));
            if (match is not null) return match;
        }
        return null;
    }

    private static bool TryGetGmailSelection(JsonElement content, out V2GmailMessageSelection selection)
    {
        selection = default!;
        if (!TryGetProviderEnvelope(content, ["gmailMessages", "gmailThreads"], out var envelope) ||
            !envelope.TryGetProperty("selection", out var selectionElement))
            return false;
        try
        {
            selection = selectionElement.Deserialize<V2GmailMessageSelection>(SemanticJson)!;
            return selection is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IEnumerable<string> GmailMessageIds(JsonElement content)
    {
        if (!TryGetProviderEnvelope(content, ["gmailMessages", "gmailThreads"], out var envelope)) yield break;
        if (envelope.TryGetProperty("resultMessageIds", out var resultIds) && resultIds.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in resultIds.EnumerateArray())
                if (value.ValueKind == JsonValueKind.String && ValidProviderIdentifier(value.GetString()))
                    yield return value.GetString()!;
            yield break;
        }
        if (envelope.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
        {
            foreach (var message in messages.EnumerateArray())
                if (TryGetString(message, "messageId", out var id) && ValidProviderIdentifier(id)) yield return id!;
            yield break;
        }
        if (!envelope.TryGetProperty("threads", out var threads) || threads.ValueKind != JsonValueKind.Array) yield break;
        foreach (var thread in threads.EnumerateArray())
        {
            if (!thread.TryGetProperty("messages", out messages) || messages.ValueKind != JsonValueKind.Array) continue;
            foreach (var message in messages.EnumerateArray())
                if (TryGetString(message, "messageId", out var id) && ValidProviderIdentifier(id)) yield return id!;
        }
    }

    private static string? GmailSender(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object) return null;
        if (content.TryGetProperty("incomingMessage", out var legacy) &&
            TryGetString(legacy, "senderAddress", out var legacyAddress)) return legacyAddress;
        if (!TryGetProviderEnvelope(content, ["gmailMessages", "gmailThreads"], out var envelope)) return null;
        if (envelope.TryGetProperty("senderAddresses", out var senderAddresses) &&
            senderAddresses.ValueKind == JsonValueKind.Array)
            return senderAddresses.EnumerateArray()
                .Where(static value => value.ValueKind == JsonValueKind.String)
                .Select(static value => value.GetString())
                .FirstOrDefault(static value => ValidProviderIdentifier(value));
        if (envelope.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            return messages.EnumerateArray().Select(static message =>
                TryGetString(message, "fromAddress", out var value) ? value : null)
                .FirstOrDefault(static value => ValidProviderIdentifier(value));
        if (!envelope.TryGetProperty("threads", out var threads) || threads.ValueKind != JsonValueKind.Array) return null;
        foreach (var thread in threads.EnumerateArray())
        {
            if (!thread.TryGetProperty("messages", out messages) || messages.ValueKind != JsonValueKind.Array) continue;
            var value = messages.EnumerateArray().Select(static message =>
                TryGetString(message, "fromAddress", out var address) ? address : null)
                .FirstOrDefault(static address => ValidProviderIdentifier(address));
            if (value is not null) return value;
        }
        return null;
    }

    private string? TryGetLatestGmailSender(V2RequestContext context) =>
        LatestGrounding(context, "gmail.") is { } grounding ? GmailSender(grounding.Content) : null;

    private bool TryGetSalesforceContinuation(V2RequestContext context, out string value)
    {
        value = string.Empty;
        var grounding = LatestGrounding(context, "salesforce.");
        if (grounding is null || !TryGetString(grounding.Content, "continuation", out var candidate) ||
            !Guid.TryParseExact(candidate, "N", out _)) return false;
        value = candidate!;
        return true;
    }

    private string? TryGetLatestSalesforceEntity(V2RequestContext context)
    {
        var grounding = LatestGrounding(context, "salesforce.");
        return grounding is not null && TryGetString(grounding.Content, "entity", out var entity) &&
               ValidProviderIdentifier(entity)
            ? entity
            : null;
    }

    private bool TryGetSalesforceRecord(
        V2RequestContext context,
        int? ordinal,
        out V2SalesforceResolvedRecord record,
        out int resultCount)
    {
        record = default!;
        resultCount = 0;
        var grounding = LatestGrounding(context, "salesforce.");
        if (grounding is null || grounding.Content.ValueKind != JsonValueKind.Object) return false;
        var entity = TryGetString(grounding.Content, "entity", out var entityValue) && ValidProviderIdentifier(entityValue)
            ? entityValue
            : null;
        var recordIds = SalesforceRecordIds(grounding.Content)
            .Distinct(StringComparer.Ordinal)
            .Take(20)
            .ToArray();
        resultCount = TryGetInt32(grounding.Content, "resultCount", out var count)
            ? Math.Max(count, recordIds.Length)
            : recordIds.Length;
        var index = ordinal is { } requestedOrdinal ? requestedOrdinal - 1 : 0;
        if (index < 0 || index >= recordIds.Length || ordinal is null && resultCount != 1) return false;
        record = new V2SalesforceResolvedRecord(
            new V2SalesforceSemanticEntity(entity ?? "record"),
            recordIds[index]);
        return true;
    }

    private static V2ToolOutcome SalesforceOutcome(
        V2SalesforceReadResult result,
        string resultField,
        string? entity,
        string? matchedSender = null)
    {
        if (result.Status != V2SalesforceReadStatus.Success)
            return SalesforceFailure(result);
        JsonElement content;
        try { content = JsonElement.Parse(result.Content ?? "[]"); }
        catch (JsonException) { content = JsonSerializer.SerializeToElement(result.Content ?? string.Empty); }
        var continuationValue = result.Continuation is { Value: var opaque } && Guid.TryParseExact(opaque, "N", out _)
            ? opaque
            : null;
        var envelope = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [resultField] = content,
            ["entity"] = entity,
            ["resultCount"] = result.ReturnedCount,
            ["hasMore"] = continuationValue is not null
        };
        if (matchedSender is not null) envelope["matchedGmailSender"] = matchedSender;
        var recordIds = SalesforceRecordIds(content).Distinct(StringComparer.Ordinal).Take(20).ToArray();
        var grounding = JsonSerializer.SerializeToElement(new
        {
            entity,
            recordIds,
            resultCount = result.ReturnedCount,
            hasMore = continuationValue is not null,
            continuation = continuationValue,
            matchedGmailSender = matchedSender
        }, SemanticJson);
        return new V2ToolOutcome(
            V2ToolOutcomeKind.Success,
            JsonSerializer.SerializeToElement(envelope, SemanticJson),
            GroundingContent: grounding);
    }

    private static IEnumerable<string> SalesforceRecordIds(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            if (ValidSalesforceRecordId(value.GetString()))
            {
                yield return value.GetString()!;
                yield break;
            }
            JsonElement parsed;
            try { parsed = JsonElement.Parse(value.GetString() ?? string.Empty); }
            catch (JsonException) { yield break; }
            foreach (var id in SalesforceRecordIds(parsed)) yield return id;
            yield break;
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                foreach (var id in SalesforceRecordIds(item))
                    yield return id;
            }
            yield break;
        }
        if (value.ValueKind != JsonValueKind.Object) yield break;
        if (value.TryGetProperty("RecordId", out var recordId) && recordId.ValueKind == JsonValueKind.String &&
            ValidSalesforceRecordId(recordId.GetString()))
            yield return recordId.GetString()!;
        foreach (var property in value.EnumerateObject())
        {
            if (property.Name is "Fields" or "attributes" or "RecordId") continue;
            foreach (var id in SalesforceRecordIds(property.Value)) yield return id;
        }
    }

    private static V2ToolOutcome SalesforceFailure(V2SalesforceReadResult result) => result.Status switch
    {
        V2SalesforceReadStatus.NeedsAuth when IsAllowedSalesforceAuthorizationUrl(result.ConnectionUrl) => new V2ToolOutcome(
            V2ToolOutcomeKind.NeedsAuth,
            SafeReason: SafeProviderReason(result.SafeReason, "Connect your Salesforce account to let INO read Salesforce."),
            Action: new V2ToolAction("openUrl", "Connect Salesforce", result.ConnectionUrl!)),
        V2SalesforceReadStatus.NeedsAuth => new V2ToolOutcome(
            V2ToolOutcomeKind.PermanentFailure,
            SafeReason: "Salesforce connection is unavailable right now."),
        V2SalesforceReadStatus.ConfigurationMissing => new V2ToolOutcome(
            V2ToolOutcomeKind.PermanentFailure,
            SafeReason: SafeProviderReason(result.SafeReason, "Salesforce application configuration is missing.")),
        V2SalesforceReadStatus.AccessDenied => new V2ToolOutcome(
            V2ToolOutcomeKind.Denied,
            SafeReason: "Salesforce denied access to that object or field for the connected user."),
        V2SalesforceReadStatus.InvalidRequest => InvalidTypedRequest(
            SafeProviderReason(result.SafeReason, "That Salesforce request is not supported by the accessible schema.")),
        V2SalesforceReadStatus.ContinuationExpired => InvalidTypedRequest(
            "That Salesforce continuation expired. Run the bounded read again."),
        V2SalesforceReadStatus.LimitReached => InvalidTypedRequest(
            SafeProviderReason(result.SafeReason, "The Salesforce safety limit was reached. Narrow the request.")),
        _ => new V2ToolOutcome(
            V2ToolOutcomeKind.RetryableFailure,
            SafeReason: "I couldn’t read Salesforce right now. Please try again later.")
    };

    private static V2ToolOutcome InvalidTypedRequest(string safeReason) =>
        new(V2ToolOutcomeKind.PermanentFailure, SafeReason: SafeProviderReason(safeReason, "That typed request is unavailable."));

    private static bool TryGetProviderEnvelope(JsonElement content, string[] names, out JsonElement envelope)
    {
        envelope = default;
        if (content.ValueKind != JsonValueKind.Object) return false;
        foreach (var name in names)
            if (content.TryGetProperty(name, out envelope) && envelope.ValueKind == JsonValueKind.Object) return true;
        return false;
    }

    private static bool TryGetString(JsonElement value, string propertyName, out string? result)
    {
        result = null;
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String) return false;
        result = property.GetString();
        return true;
    }

    private static bool TryGetInt32(JsonElement value, string propertyName, out int result)
    {
        result = 0;
        if (value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty(propertyName, out var direct) && direct.TryGetInt32(out result)) return true;
        if (value.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in value.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.Object &&
                property.Value.TryGetProperty(propertyName, out var nested) && nested.TryGetInt32(out result)) return true;
        return false;
    }

    private static bool TryGetBoolean(JsonElement value, string propertyName, out bool result)
    {
        result = false;
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(propertyName, out var direct) &&
            direct.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            result = direct.GetBoolean();
            return true;
        }
        if (value.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object ||
                !property.Value.TryGetProperty(propertyName, out var nested) ||
                nested.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) continue;
            result = nested.GetBoolean();
            return true;
        }
        return false;
    }

    private static bool TryGetTimeRange(
        V2SemanticTimeRange range,
        out DateTimeOffset from,
        out DateTimeOffset until)
    {
        var now = DateTimeOffset.UtcNow;
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var week = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var quarterMonth = ((today.Month - 1) / 3) * 3 + 1;
        var quarter = new DateTimeOffset(today.Year, quarterMonth, 1, 0, 0, 0, TimeSpan.Zero);
        (from, until) = range switch
        {
            V2SemanticTimeRange.Today => (today, today.AddDays(1)),
            V2SemanticTimeRange.Yesterday => (today.AddDays(-1), today),
            V2SemanticTimeRange.CurrentWeek => (week, week.AddDays(7)),
            V2SemanticTimeRange.PreviousWeek => (week.AddDays(-7), week),
            V2SemanticTimeRange.CurrentMonth => (new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1)),
            V2SemanticTimeRange.PreviousMonth => (new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(-1),
                new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, TimeSpan.Zero)),
            V2SemanticTimeRange.CurrentQuarter => (quarter, quarter.AddMonths(3)),
            V2SemanticTimeRange.PreviousQuarter => (quarter.AddMonths(-3), quarter),
            V2SemanticTimeRange.CurrentYear => (new DateTimeOffset(today.Year, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(today.Year + 1, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            _ => (default, default)
        };
        return range != V2SemanticTimeRange.None;
    }

    private static bool IsSemanticTool(string toolId) => toolId is
        V2GmailTools.ReadMessages or
        V2GmailTools.ReadMailboxOverview or
        V2GmailTools.ReadThreads or
        V2GmailTools.SummarizeThread or
        V2SalesforceTools.DiscoverObjects or
        V2SalesforceTools.ReadRecords or
        V2SalesforceTools.SearchRecords or
        V2SalesforceTools.AggregateRecords or
        V2SalesforceTools.ContinueRecords or
        V2SalesforceTools.PreviewMutation or
        V2CrossProviderTools.MatchSalesforceAccountToGmailSender;

    private static bool TryParseSemanticIntent(
        string toolId,
        JsonElement input,
        out V2SemanticIntentProposal proposal)
    {
        proposal = default!;
        if (input.ValueKind != JsonValueKind.Object || input.GetRawText().Length > 16 * 1024) return false;
        try { proposal = input.Deserialize<V2SemanticIntentProposal>(SemanticJson)!; }
        catch (JsonException) { return false; }
        if (proposal is null || proposal.Limit is < 1 or > V2GmailTools.MaximumResultCount ||
            proposal.Ordinal is < 1 or > V2GmailTools.MaximumResultCount ||
            proposal.Filters is { Count: > 8 } || proposal.Sorts is { Count: > 8 } ||
            !ValidSemanticText(proposal.Entity, required: false) ||
            !ValidSemanticText(proposal.SearchText, required: false) ||
            !ValidSemanticText(proposal.Clarification, required: false) ||
            proposal.Filters?.Any(static filter =>
                !ValidSemanticText(filter.Field, required: true) || !ValidSemanticText(filter.Value, required: false)) == true ||
            proposal.Sorts?.Any(static sort => !ValidSemanticText(sort.Field, required: true)) == true ||
            (proposal.Aggregate is { } aggregate &&
             (!ValidSemanticText(aggregate.Field, required: false) ||
              !ValidSemanticText(aggregate.GroupBy, required: false))))
            return false;
        return string.Equals(ExpectedSemanticTool(proposal), toolId, StringComparison.Ordinal);
    }

    private static string? ExpectedSemanticTool(V2SemanticIntentProposal proposal) => proposal.Provider switch
    {
        V2SemanticProvider.Gmail => proposal.Operation switch
        {
            V2SemanticOperation.List or V2SemanticOperation.Refine or V2SemanticOperation.Previous => V2GmailTools.ReadMessages,
            V2SemanticOperation.Overview => V2GmailTools.ReadMailboxOverview,
            V2SemanticOperation.Threads => V2GmailTools.ReadThreads,
            V2SemanticOperation.Summarize => V2GmailTools.SummarizeThread,
            _ => null
        },
        V2SemanticProvider.Salesforce => proposal.Operation switch
        {
            V2SemanticOperation.Discover => V2SalesforceTools.DiscoverObjects,
            V2SemanticOperation.Search => V2SalesforceTools.SearchRecords,
            V2SemanticOperation.Aggregate => V2SalesforceTools.AggregateRecords,
            V2SemanticOperation.NextPage => V2SalesforceTools.ContinueRecords,
            V2SemanticOperation.MutationPreview => V2SalesforceTools.PreviewMutation,
            V2SemanticOperation.List or V2SemanticOperation.Refine or V2SemanticOperation.Related or
                V2SemanticOperation.Details or V2SemanticOperation.Previous => V2SalesforceTools.ReadRecords,
            _ => null
        },
        V2SemanticProvider.CrossProvider when proposal.Operation == V2SemanticOperation.Match &&
                                                 proposal.Reference == V2SemanticReference.LatestGmailSender =>
            V2CrossProviderTools.MatchSalesforceAccountToGmailSender,
        _ => null
    };

    private static bool ValidSemanticText(string? value, bool required) =>
        value is null ? !required : value.Trim().Length is > 0 and <= MaximumSemanticText && !value.Any(char.IsControl);

    private static bool ValidProviderIdentifier(string? value) =>
        value is { Length: > 0 and <= MaximumSemanticText } && !value.Any(char.IsControl);

    private static bool ValidSalesforceRecordId(string? value) =>
        value is { Length: 15 or 18 } && value.All(char.IsLetterOrDigit);

    private static string NormalizeSemanticName(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool IsAllAccessible(string? entity) =>
        string.IsNullOrWhiteSpace(entity) || NormalizeSemanticName(entity) is "all" or "allaccessible";

    private static string SafeProviderReason(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl) ||
            value.Contains("://", StringComparison.Ordinal)) return fallback;
        return value.Trim();
    }

    private static JsonSerializerOptions CreateSemanticJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static string GmailMailboxStatus(V2GmailMailboxState state) => state switch
    {
        V2GmailMailboxState.SenderAvailable => "senderAvailable",
        V2GmailMailboxState.EmptyInbox => "emptyInbox",
        V2GmailMailboxState.SenderUnavailable => "senderUnavailable",
        _ => "positionUnavailable"
    };

    private static bool TryParseGmailRequest(JsonElement input, out V2GmailReadRequest request)
    {
        request = new V2GmailReadRequest(-1);
        if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Any(static property => property.Name is not
                ("offset" or "anchorMessageId" or "anchorInternalDate" or "traversalDepth" or "requiresAnchor")))
            return false;
        try
        {
            request = input.Deserialize<V2GmailReadRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
        catch (JsonException)
        {
            return false;
        }
        return request is not null &&
               request.Offset is >= 0 and <= V2GmailTools.MaximumOffset &&
               request.TraversalDepth is >= 0 and <= V2GmailTools.MaximumOffset + 1 &&
               (request.AnchorMessageId is null
                   ? request.AnchorInternalDate is null &&
                     (!request.RequiresAnchor || request.TraversalDepth == V2GmailTools.MaximumOffset + 1) &&
                     (request.RequiresAnchor || request.TraversalDepth == request.Offset)
                   : request.RequiresAnchor && request.Offset == 1 && request.AnchorInternalDate is >= 0 &&
                     request.AnchorMessageId.Length is > 0 and <= 256);
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
    private const string UngroundedMailboxReason = "I couldn’t verify that mailbox claim from a successful Gmail result, so I won’t guess.";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly Regex EmailAddress = new(
        @"(?<![\p{L}\p{N}._%+-])[a-z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+(?![\p{L}\p{N}._%+-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex MailboxSenderClaim = new(
        @"\b(?:gmail|email|mailbox|incoming message)\b.{0,120}\b(?:sent by|sender|from)\b|" +
        @"\b(?:sent by|sender)\b.{0,120}\b(?:gmail|email|mailbox|message)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex MailboxReference = new(
        @"\b(?:gmail|email|mailbox|incoming message|sender)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
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
        var typedGmailResponse = toolOutcomes
            .Select(static outcome => ComposeGmailMetadata(outcome.Content))
            .FirstOrDefault(static text => text is not null);
        if (typedGmailResponse is not null)
            return Task.FromResult(typedGmailResponse);
        var groundedGmailResponse = toolOutcomes
            .Select(static outcome => ComposeIncomingGmail(outcome.Content))
            .FirstOrDefault(static text => text is not null);
        if (groundedGmailResponse is not null)
            return Task.FromResult(groundedGmailResponse);
        var groundedSalesforceResponse = toolOutcomes
            .Select(static outcome => ComposeSalesforce(outcome.Content))
            .FirstOrDefault(static text => text is not null);
        if (groundedSalesforceResponse is not null)
            return Task.FromResult(groundedSalesforceResponse);
        if (string.IsNullOrWhiteSpace(response.Text))
            throw new InvalidOperationException("The configured model returned no answer.");
        var text = response.Text.Trim();
        if (MailboxSenderClaim.IsMatch(text) ||
            (EmailAddress.IsMatch(text) && MailboxReference.IsMatch(text)))
            return Task.FromResult(UngroundedMailboxReason);
        if (UnsafeAddress.IsMatch(text) || UnsafeTerm.IsMatch(text) || ContainsSensitiveContextValue(text, context))
            throw new InvalidOperationException("The configured model returned an answer that is unsafe to display.");
        return Task.FromResult(text);
    }

    private static string? ComposeGmailMetadata(JsonElement? content)
    {
        if (content is not { ValueKind: JsonValueKind.Object } root) return null;
        if (root.TryGetProperty("gmailMailboxOverview", out var overview) && overview.ValueKind == JsonValueKind.Object)
        {
            return "Gmail mailbox overview: " +
                   $"{ReadBoundedInt(overview, "inboxMessages")} inbox messages, " +
                   $"{ReadBoundedInt(overview, "unreadInboxMessages")} unread, " +
                   $"{ReadBoundedInt(overview, "inboxThreads")} inbox threads, and " +
                   $"{ReadBoundedInt(overview, "unreadInboxThreads")} unread threads.";
        }
        if (root.TryGetProperty("gmailMessages", out var messageEnvelope) &&
            messageEnvelope.ValueKind == JsonValueKind.Object)
            return ComposeGmailMessages(messageEnvelope);
        if (root.TryGetProperty("gmailThreads", out var threadEnvelope) &&
            threadEnvelope.ValueKind == JsonValueKind.Object)
            return ComposeGmailThreads(threadEnvelope);
        return null;
    }

    private static string ComposeGmailMessages(JsonElement envelope)
    {
        if (!envelope.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("The Gmail metadata tool returned an invalid message list.");
        var rows = messages.EnumerateArray().Take(V2GmailTools.MaximumResultCount).Select((message, index) =>
        {
            var subject = ReadProviderString(message, "subject") ?? "(no subject)";
            var sender = ReadProviderString(message, "from") ??
                         ReadProviderString(message, "fromAddress") ?? "sender unavailable";
            var date = ReadGmailDate(message);
            var readState = message.TryGetProperty("isRead", out var read) && read.ValueKind == JsonValueKind.True
                ? "read"
                : "unread";
            return $"{index + 1}. Subject: “{SafeProviderText(subject, 180)}” — from {SafeProviderText(sender, 220)}; {date}; {readState}.";
        }).ToArray();
        if (rows.Length == 0) return "No matching Gmail messages were found within the bounded metadata read.";
        return "Gmail messages:\n" + string.Join("\n", rows) + CoverageNote(envelope);
    }

    private static string ComposeGmailThreads(JsonElement envelope)
    {
        if (!envelope.TryGetProperty("threads", out var threads) || threads.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("The Gmail metadata tool returned an invalid thread list.");
        var rows = threads.EnumerateArray().Take(V2GmailTools.MaximumResultCount).Select((thread, index) =>
        {
            var subject = ReadProviderString(thread, "subject") ?? "(no subject)";
            var count = ReadBoundedInt(thread, "matchingMessageCount");
            var unread = thread.TryGetProperty("hasUnread", out var unreadElement) && unreadElement.ValueKind == JsonValueKind.True
                ? "; has unread mail"
                : string.Empty;
            var participants = thread.TryGetProperty("participantAddresses", out var values) && values.ValueKind == JsonValueKind.Array
                ? string.Join(", ", values.EnumerateArray().Take(6)
                    .Where(static value => value.ValueKind == JsonValueKind.String)
                    .Select(static value => SafeProviderText(value.GetString() ?? string.Empty, 120)))
                : "participants unavailable";
            return $"{index + 1}. Thread: “{SafeProviderText(subject, 180)}” — {count} matching message(s); {participants}{unread}.";
        }).ToArray();
        if (rows.Length == 0) return "No matching Gmail threads were found within the bounded metadata read.";
        return "Gmail threads:\n" + string.Join("\n", rows) + CoverageNote(envelope);
    }

    private static string CoverageNote(JsonElement envelope)
    {
        if (!envelope.TryGetProperty("coverage", out var coverage) || coverage.ValueKind != JsonValueKind.Object)
            return string.Empty;
        var limited = coverage.TryGetProperty("candidateLimitReached", out var candidateLimit) &&
                      candidateLimit.ValueKind == JsonValueKind.True;
        var exhausted = coverage.TryGetProperty("providerExhausted", out var providerExhausted) &&
                        providerExhausted.ValueKind == JsonValueKind.True;
        return limited || !exhausted
            ? "\nThis is a bounded partial result; narrow the request to search more precisely."
            : string.Empty;
    }

    private static string ReadGmailDate(JsonElement message)
    {
        if (!message.TryGetProperty("internalDate", out var value) || !value.TryGetInt64(out var milliseconds))
            return "date unavailable";
        try { return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture); }
        catch (ArgumentOutOfRangeException) { return "date unavailable"; }
    }

    private static string? ReadProviderString(JsonElement value, string propertyName) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int ReadBoundedInt(JsonElement value, string propertyName) =>
        value.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var number) && number >= 0
            ? number
            : 0;

    private static string? ComposeIncomingGmail(JsonElement? content)
    {
        if (content is not { ValueKind: JsonValueKind.Object } root ||
            !root.TryGetProperty("incomingMessage", out var message) ||
            message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("status", out var statusElement) ||
            statusElement.ValueKind != JsonValueKind.String)
            return null;

        return statusElement.GetString() switch
        {
            "emptyInbox" => "No incoming Gmail messages were found.",
            "positionUnavailable" => "I couldn’t safely resolve that incoming Gmail position. Ask for the latest incoming email to start again.",
            "senderUnavailable" => ComposeUnavailableSender(message),
            "senderAvailable" => ComposeAvailableSender(message),
            _ => throw new InvalidOperationException("The Gmail tool returned an unknown mailbox state.")
        };
    }

    private static string ComposeAvailableSender(JsonElement message)
    {
        var sender = message.TryGetProperty("sender", out var senderElement) && senderElement.ValueKind == JsonValueKind.String
            ? senderElement.GetString()
            : null;
        var address = message.TryGetProperty("senderAddress", out var addressElement) && addressElement.ValueKind == JsonValueKind.String
            ? addressElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(sender) || string.IsNullOrWhiteSpace(address) ||
            !sender.Contains(address, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Gmail tool returned incomplete sender metadata.");
        var response = $"{PositionPrefix(message)} was sent by {sender}.";
        if (UnsafeAddress.IsMatch(response))
            throw new InvalidOperationException("The Gmail tool returned unsafe sender metadata.");
        return response;
    }

    private static string ComposeUnavailableSender(JsonElement message) =>
        $"{PositionPrefix(message)}’s sender metadata was unavailable.";

    private static string PositionPrefix(JsonElement message)
    {
        var anchored = message.TryGetProperty("anchoredPrevious", out var anchoredElement) &&
                       anchoredElement.ValueKind is JsonValueKind.True;
        if (anchored) return "The incoming email immediately before that";
        var depth = message.TryGetProperty("traversalDepth", out var depthElement) && depthElement.TryGetInt32(out var value)
            ? value
            : 0;
        return depth switch
        {
            0 => "The latest incoming email",
            1 => "The second-to-last incoming email",
            2 => "The third-to-last incoming email",
            3 => "The fourth-to-last incoming email",
            4 => "The fifth-to-last incoming email",
            _ => throw new InvalidOperationException("The Gmail tool returned an invalid traversal depth.")
        };
    }

    private static string? ComposeSalesforce(JsonElement? content)
    {
        if (content is not { ValueKind: JsonValueKind.Object } root) return null;
        foreach (var property in root.EnumerateObject())
        {
            var title = property.Name switch
            {
                "latestAccount" => "Latest Salesforce account",
                "recentAccounts" => "Salesforce accounts",
                "recentContacts" => "Salesforce contacts",
                "currentProfile" => "Salesforce profile",
                "crmSchema" => "Accessible Salesforce schema",
                "salesforceRecords" => "Salesforce records",
                "salesforceSearch" => "Salesforce search results",
                "salesforceAggregate" => "Salesforce aggregate",
                "salesforceObjects" => "Accessible Salesforce objects",
                "salesforceMutationPreview" => "Salesforce mutation preview (no change made)",
                _ => null
            };
            if (title is null) continue;
            var value = property.Value;
            if (value.ValueKind == JsonValueKind.String)
            {
                var raw = value.GetString() ?? string.Empty;
                if (raw.Length > 64 * 1024)
                    throw new InvalidOperationException("The Salesforce tool returned an oversized result.");
                try { value = JsonElement.Parse(raw); }
                catch (JsonException) { return title + ": " + SafeProviderText(raw, 512); }
            }
            return title + ":\n" + FormatProviderValue(value, depth: 0);
        }
        return null;
    }

    private static string FormatProviderValue(JsonElement value, int depth)
    {
        if (depth > 4) return "[nested value omitted]";
        return value.ValueKind switch
        {
            JsonValueKind.Array => value.GetArrayLength() == 0
                ? "No matching records."
                : string.Join("\n", value.EnumerateArray().Take(10).Select((item, index) =>
                    $"{index + 1}. {FormatProviderValue(item, depth + 1)}")),
            JsonValueKind.Object => string.Join("; ", value.EnumerateObject()
                .Where(static property => !HiddenSalesforceField(property.Name))
                .Take(24)
                .Select(property => $"{SafeProviderText(property.Name, 80)}: {FormatProviderValue(property.Value, depth + 1)}")),
            JsonValueKind.String => SafeProviderText(value.GetString() ?? string.Empty, 512),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            JsonValueKind.Null or JsonValueKind.Undefined => "—",
            _ => "—"
        };
    }

    private static bool HiddenSalesforceField(string name) =>
        string.Equals(name, "attributes", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "nextRecordsUrl", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase);

    private static string SafeProviderText(string value, int maximumLength)
    {
        var normalized = new string(value.Select(static character => char.IsControl(character) ? ' ' : character).ToArray()).Trim();
        if (UnsafeAddress.IsMatch(normalized)) normalized = "[external address omitted]";
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength] + "…";
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
