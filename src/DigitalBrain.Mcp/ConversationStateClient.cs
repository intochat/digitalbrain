using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using Orleans;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

public sealed class ConversationStateClient(
    IClusterClient cluster,
    IActiveConversationFeed activeConversationFeed,
    TimeProvider timeProvider) : IInoConversationStore, IConversationLifecycleState
{
    private const string FeedOutboxKind = "surface-feed";
    private static readonly TimeSpan OperationLease = TimeSpan.FromMinutes(2);
    private readonly string _leaseOwner = "mcp-" + Guid.NewGuid().ToString("N");

    public async Task<InoConversationSnapshot> ReadAsync(
        RuntimeRequestContext context,
        CancellationToken cancellationToken = default)
    {
        context = await ResolveContextAsync(context, cancellationToken).ConfigureAwait(false);
        var state = await ReadStateAsync(context, cancellationToken).ConfigureAwait(false);
        return ToSnapshot(context, state);
    }

    public async Task<InoConversationSnapshot> BeginAsync(
        RuntimeRequestContext context,
        string commandId,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(commandId) || commandId.Length > 256)
            throw new ArgumentException("A bounded command id is required.", nameof(commandId));
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 16_000)
            throw new ArgumentException("A bounded prompt is required.", nameof(prompt));
        context = await ResolveContextAsync(context, cancellationToken).ConfigureAwait(false);
        var neuron = Conversation(context);
        var state = await EnsureInitializedAsync(context, neuron, cancellationToken).ConfigureAwait(false);
        var operationId = OperationId(context, commandId);
        var inputHash = Hash(prompt.Trim());
        state = await RetryConflictAsync(
            neuron,
            state,
            current => neuron.BeginOperationAsync(
                current.Revision,
                commandId,
                inputHash,
                operationId,
                prompt.Trim(),
                timeProvider.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);
        return ToSnapshot(context, state);
    }

    public async Task<InoConversationSnapshot> TransitionAsync(
        RuntimeRequestContext context,
        string commandId,
        string stateName,
        CancellationToken cancellationToken = default)
    {
        if (stateName is not (InoConversationStates.Running or InoConversationStates.Responding))
            throw new ArgumentOutOfRangeException(nameof(stateName));
        context = await ResolveContextAsync(context, cancellationToken).ConfigureAwait(false);
        var neuron = Conversation(context);
        var state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var operation = RequiredOperation(state, commandId);
        if (stateName == InoConversationStates.Responding)
            return ToSnapshot(context, state);
        var claim = await RetryConflictAsync(
            neuron,
            state,
            current => neuron.TryClaimOperationAsync(
                current.Revision,
                operation.OperationId,
                _leaseOwner,
                timeProvider.GetUtcNow(),
                OperationLease),
            cancellationToken).ConfigureAwait(false);
        if (!claim.Claimed)
            throw new ConversationOperationLeaseUnavailableException();
        return ToSnapshot(context, claim.State);
    }

    internal async Task<bool> TryClaimAuthorizationAsync(
        RuntimeRequestContext context,
        string commandId,
        string authorizationAttemptId,
        CancellationToken cancellationToken)
    {
        context = await ResolveContextAsync(context, cancellationToken).ConfigureAwait(false);
        var neuron = Conversation(context);
        var state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var operation = RequiredOperation(state, commandId);
        try
        {
            var claim = await neuron.TryClaimAuthorizationAsync(
                state.Revision,
                operation.OperationId,
                authorizationAttemptId,
                _leaseOwner,
                timeProvider.GetUtcNow(),
                OperationLease).WaitAsync(cancellationToken).ConfigureAwait(false);
            return claim.Claimed;
        }
        catch (RuntimeStateConflictException)
        {
            return false;
        }
    }

    public async Task<InoConversationSnapshot> CompleteAsync(
        RuntimeRequestContext context,
        string commandId,
        string response,
        ToolAction? action = null,
        ToolGrounding? grounding = null,
        IReadOnlyList<ToolGrounding>? groundings = null,
        CancellationToken cancellationToken = default)
    {
        ValidateAssistantText(response);
        context = await ResolveContextAsync(context, cancellationToken).ConfigureAwait(false);
        var neuron = Conversation(context);
        var state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var operation = RequiredOperation(state, commandId);
        var now = timeProvider.GetUtcNow();
        var projection = CreateProjection(
            operation.OperationId,
            response.Trim(),
            action,
            grounding,
            groundings,
            authorization: null);
        var outbox = CreateOutbox(operation.OperationId, projection, now);
        state = await RetryConflictAsync(
            neuron,
            state,
            current => neuron.CompleteWithAssistantAsync(
                current.Revision,
                operation.OperationId,
                ConversationOperationStatus.Succeeded,
                ConversationTerminalPolicy.NeverRetry,
                null,
                response.Trim(),
                outbox,
                now),
            cancellationToken).ConfigureAwait(false);
        return ToSnapshot(context, state);
    }

    public async Task<InoConversationSnapshot> AwaitAuthorizationAsync(
        RuntimeRequestContext context,
        string commandId,
        string response,
        ToolAction action,
        ExternalAuthorizationContinuation authorization,
        CancellationToken cancellationToken = default)
    {
        ValidateAssistantText(response);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(authorization);
        if (!authorization.IsValid() ||
            !string.Equals(action.Kind, "openUrl", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(action.Label) ||
            action.Label.Length > 64 ||
            action.Label.Any(char.IsControl) ||
            !OAuthCallbackPaths.TryParseInternalStartPath(
                action.Target,
                authorization.Provider,
                out var flowReference))
            throw new InvalidOperationException("The external authorization continuation is not an internal bounded flow.");
        context = await ResolveContextAsync(context, cancellationToken).ConfigureAwait(false);
        var neuron = Conversation(context);
        var state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var operation = RequiredOperation(state, commandId);
        var now = timeProvider.GetUtcNow();
        var suspended = new SuspendedInvocation(
            authorization.Provider,
            authorization.Invocation.ToolId,
            Encoding.UTF8.GetBytes(authorization.Invocation.Input.GetRawText()),
            authorization.AttemptId,
            authorization.ExpiresAt,
            flowReference);
        var safeAction = new ToolAction(
            "openUrl",
            action.Label,
            OAuthCallbackPaths.CreateInternalStartPath(authorization.Provider, flowReference));
        var projection = CreateProjection(
            operation.OperationId,
            response.Trim(),
            safeAction,
            grounding: null,
            groundings: null,
            authorization);
        var outbox = CreateOutbox(operation.OperationId, projection, now);
        state = await RetryConflictAsync(
            neuron,
            state,
            current => neuron.SuspendAuthorizationWithAssistantAsync(
                current.Revision,
                operation.OperationId,
                suspended,
                response.Trim(),
                outbox,
                now),
            cancellationToken).ConfigureAwait(false);
        return ToSnapshot(context, state);
    }

    public async Task<InoConversationSnapshot> FailAsync(
        RuntimeRequestContext context,
        string commandId,
        string safeReason,
        bool retryable,
        CancellationToken cancellationToken = default)
    {
        var reason = string.IsNullOrWhiteSpace(safeReason)
            ? "I couldn’t finish that response."
            : safeReason.Trim();
        if (reason.Length > 256) reason = reason[..256];
        context = await ResolveContextAsync(context, cancellationToken).ConfigureAwait(false);
        var neuron = Conversation(context);
        var state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var operation = RequiredOperation(state, commandId);
        var now = timeProvider.GetUtcNow();
        if (retryable)
        {
            var delay = Backoff(operation.Attempt);
            state = await RetryConflictAsync(
                neuron,
                state,
                current => neuron.ScheduleRetryAsync(
                    current.Revision,
                    operation.OperationId,
                    now.Add(delay),
                    reason,
                    now),
                cancellationToken).ConfigureAwait(false);
            return ToSnapshot(context, state);
        }
        var projection = CreateProjection(
            operation.OperationId,
            reason,
            action: null,
            grounding: null,
            groundings: null,
            authorization: null);
        var outbox = CreateOutbox(operation.OperationId, projection, now);
        state = await RetryConflictAsync(
            neuron,
            state,
            current => neuron.CompleteWithAssistantAsync(
                current.Revision,
                operation.OperationId,
                ConversationOperationStatus.Failed,
                ConversationTerminalPolicy.NeverRetry,
                reason,
                reason,
                outbox,
                now),
            cancellationToken).ConfigureAwait(false);
        return ToSnapshot(context, state);
    }

    public async Task<InoConversationSnapshot> RecordOutcomeUnknownAsync(
        RuntimeRequestContext context,
        string commandId,
        string safeReason,
        CancellationToken cancellationToken = default)
    {
        var reason = string.IsNullOrWhiteSpace(safeReason)
            ? "I couldn’t confirm the previous outcome. Review it before trying again."
            : safeReason.Trim();
        if (reason.Length > 256) reason = reason[..256];
        context = await ResolveContextAsync(context, cancellationToken).ConfigureAwait(false);
        var neuron = Conversation(context);
        var state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var operation = RequiredOperation(state, commandId);
        var now = timeProvider.GetUtcNow();
        var projection = CreateProjection(
            operation.OperationId,
            reason,
            action: null,
            grounding: null,
            groundings: null,
            authorization: null);
        state = await RetryConflictAsync(
            neuron,
            state,
            current => neuron.CompleteWithAssistantAsync(
                current.Revision,
                operation.OperationId,
                ConversationOperationStatus.OutcomeUnknown,
                ConversationTerminalPolicy.ManualIntervention,
                reason,
                reason,
                CreateOutbox(operation.OperationId, projection, now),
                now),
            cancellationToken).ConfigureAwait(false);
        return ToSnapshot(context, state);
    }

    public async Task<InoConversationSnapshot> EnsureConversationAsync(
        RuntimeRequestContext context,
        string conversationId,
        CancellationToken cancellationToken)
    {
        DemandConversationId(conversationId);
        context = context with { ConversationId = conversationId };
        var neuron = Conversation(context);
        var state = await EnsureInitializedAsync(context, neuron, cancellationToken).ConfigureAwait(false);
        return ToSnapshot(context, state);
    }

    public async Task TombstoneConversationAsync(
        RuntimeRequestContext context,
        string conversationId,
        string reason,
        CancellationToken cancellationToken)
    {
        DemandConversationId(conversationId);
        context = context with { ConversationId = conversationId };
        var neuron = Conversation(context);
        var state = await EnsureInitializedAsync(context, neuron, cancellationToken).ConfigureAwait(false);
        await RetryConflictAsync(
            neuron,
            state,
            current => neuron.TombstoneAsync(
                current.Revision,
                timeProvider.GetUtcNow(),
                reason),
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<RuntimeRequestContext> ResolveContextAsync(
        RuntimeRequestContext context,
        CancellationToken cancellationToken)
    {
        if (context.ConversationId is { } supplied)
        {
            DemandConversationId(supplied);
            return context;
        }
        var active = await activeConversationFeed.ResolveActiveConversationIdAsync(context, cancellationToken)
            .ConfigureAwait(false);
        DemandConversationId(active);
        return context with { ConversationId = active };
    }

    private async Task<ConversationState> ReadStateAsync(
        RuntimeRequestContext context,
        CancellationToken cancellationToken)
    {
        var state = await Conversation(context).ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        return state.Identity is null ? ConversationState.Empty() : state;
    }

    private async Task<ConversationState> EnsureInitializedAsync(
        RuntimeRequestContext context,
        IConversationNeuron neuron,
        CancellationToken cancellationToken)
    {
        var state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var identity = Identity(context);
        if (state.Identity is not null)
        {
            if (state.Identity != identity)
                throw new UnauthorizedAccessException("Conversation identity does not match the authenticated scope.");
            return state;
        }
        try
        {
            return await neuron.InitializeAsync(state.Revision, identity)
                .WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (RuntimeStateConflictException)
        {
            state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (state.Identity != identity)
                throw new UnauthorizedAccessException("Conversation identity does not match the authenticated scope.");
            return state;
        }
    }

    private static async Task<TResult> RetryConflictAsync<TResult>(
        IConversationNeuron neuron,
        ConversationState initial,
        Func<ConversationState, Task<TResult>> update,
        CancellationToken cancellationToken)
    {
        var state = initial;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await update(state).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (RuntimeStateConflictException) when (attempt < 2)
            {
                state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        throw new InvalidOperationException("Conversation revision retry exhausted.");
    }

    internal IConversationNeuron Conversation(RuntimeRequestContext context)
    {
        var conversationId = context.ConversationId ?? InoConversationIdentity.From(context);
        DemandConversationId(conversationId);
        return cluster.GetGrain<IConversationNeuron>(RuntimeStateKeys.Conversation(
            context.TenantId,
            context.WorkspaceId,
            context.Principal,
            conversationId));
    }

    private static ConversationIdentity Identity(RuntimeRequestContext context)
    {
        var conversationId = context.ConversationId ?? InoConversationIdentity.From(context);
        DemandConversationId(conversationId);
        return new(context.TenantId, context.WorkspaceId, context.Principal, conversationId);
    }

    internal static string OperationId(RuntimeRequestContext context, string commandId)
    {
        var defaultConversationId = InoConversationIdentity.From(context);
        var conversationScope = context.ConversationId is { } conversationId &&
                                !string.Equals(conversationId, defaultConversationId, StringComparison.Ordinal)
            ? "\0" + conversationId
            : string.Empty;
        return "runtime-op-" + Hash(RequestScope.Id(context) + conversationScope + "\0" + commandId);
    }

    internal static void DemandConversationId(string? conversationId)
    {
        if (conversationId is null || conversationId.Length != 68 ||
            !conversationId.StartsWith("ino-", StringComparison.Ordinal))
            throw new ArgumentException("Conversation ids must be canonical scoped identifiers.", nameof(conversationId));
        foreach (var character in conversationId.AsSpan(4))
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                throw new ArgumentException("Conversation ids must be canonical scoped identifiers.", nameof(conversationId));
    }

    private static ConversationOperation RequiredOperation(ConversationState state, string commandId) =>
        state.Operations.LastOrDefault(operation =>
            string.Equals(operation.CommandId, commandId, StringComparison.Ordinal))
        ?? throw new InvalidOperationException("The conversation operation was not committed.");

    private static ConversationOutboxEntry CreateOutbox(
        string operationId,
        ConversationFeedProjection projection,
        DateTimeOffset now) => new(
        "feed-" + operationId,
        FeedOutboxKind,
        JsonSerializer.SerializeToUtf8Bytes(projection),
        now,
        null);

    private static ConversationFeedProjection CreateProjection(
        string operationId,
        string text,
        ToolAction? action,
        ToolGrounding? grounding,
        IReadOnlyList<ToolGrounding>? groundings,
        ExternalAuthorizationContinuation? authorization) => new(
        operationId,
        text,
        action,
        grounding,
        groundings?.ToArray(),
        authorization?.Copy());

    internal static InoConversationSnapshot ToSnapshot(
        RuntimeRequestContext context,
        ConversationState state)
    {
        if (state.Identity is null)
            return new(context.ConversationId ?? InoConversationIdentity.From(context), 0, [], []);
        var projections = state.Outbox
            .Where(entry => string.Equals(entry.Kind, FeedOutboxKind, StringComparison.Ordinal))
            .Select(TryReadProjection)
            .Where(projection => projection is not null)
            .ToDictionary(projection => projection!.OperationId, projection => projection!, StringComparer.Ordinal);
        var operations = state.Operations.Select(operation =>
        {
            projections.TryGetValue(operation.OperationId, out var projection);
            var userText = state.Turns.LastOrDefault(turn =>
                turn.Kind == ConversationTurnKind.User &&
                string.Equals(turn.OperationId, operation.OperationId, StringComparison.Ordinal))?.Text ?? string.Empty;
            var suspended = operation.SuspendedInvocation;
            ExternalAuthorizationContinuation? continuation = null;
            ToolAction? action = projection?.Action;
            if (suspended is not null)
            {
                try
                {
                    using var input = JsonDocument.Parse(suspended.InputUtf8);
                    continuation = new(
                        suspended.Provider,
                        new ToolInvocation(suspended.ToolId, input.RootElement.Clone()),
                        suspended.AuthorizationAttemptId,
                        suspended.AuthorizationExpiresAt);
                    action = new(
                        "openUrl",
                        suspended.Provider == OAuthCallbackPaths.GoogleProvider
                            ? "Connect Google"
                            : "Connect Salesforce",
                        OAuthCallbackPaths.CreateInternalStartPath(
                            suspended.Provider,
                            suspended.AuthorizationFlowReference));
                }
                catch (JsonException)
                {
                    throw new RuntimeStateIntegrityException("invalid suspended invocation JSON");
                }
            }
            // Re-check on every materialization, not only when an authorization is freshly issued -- a
            // persisted action can predate this policy or a not-yet-expired authorization can carry a
            // poisoned target with no other self-healing path.
            if (action is not null && !OAuthCallbackPaths.IsStructurallyValidAction(action))
                action = null;
            return new InoConversationOperation(
                operation.CommandId,
                userText,
                LegacyState(operation.Status),
                operation.SafeReason,
                operation.Status == ConversationOperationStatus.RetryScheduled,
                operation.UpdatedAt,
                action,
                projection?.Grounding,
                projection?.Groundings,
                continuation);
        }).ToArray();
        var turns = state.Turns.Select(turn => new InoConversationTurn(
            turn.IdempotencyKey,
            turn.Role,
            turn.Text,
            operations.LastOrDefault(operation =>
                string.Equals(operation.CommandId, turn.IdempotencyKey, StringComparison.Ordinal))?.State
            ?? InoConversationStates.Succeeded)).ToArray();
        return new(state.Identity.ConversationId, checked((int)Math.Min(state.Revision, int.MaxValue)), turns, operations);
    }

    private static ConversationFeedProjection? TryReadProjection(ConversationOutboxEntry entry)
    {
        try { return JsonSerializer.Deserialize<ConversationFeedProjection>(entry.PayloadUtf8); }
        catch (JsonException) { throw new RuntimeStateIntegrityException("invalid conversation outbox projection"); }
    }

    private static string LegacyState(ConversationOperationStatus status) => status switch
    {
        ConversationOperationStatus.Pending => InoConversationStates.Queued,
        ConversationOperationStatus.Running => InoConversationStates.Running,
        ConversationOperationStatus.AwaitingAuthorization => InoConversationStates.AwaitingAuthorization,
        ConversationOperationStatus.RetryScheduled => InoConversationStates.Failed,
        ConversationOperationStatus.Succeeded => InoConversationStates.Succeeded,
        _ => InoConversationStates.Failed
    };

    private static void ValidateAssistantText(string response)
    {
        if (string.IsNullOrWhiteSpace(response) || response.Length > 16_000)
            throw new ArgumentException("A bounded assistant response is required.", nameof(response));
    }

    private static TimeSpan Backoff(int attempt)
    {
        var exponent = Math.Clamp(attempt, 0, 6);
        var baseSeconds = Math.Min(60, 1 << exponent);
        var jitterMilliseconds = RandomNumberGenerator.GetInt32(0, 1000);
        return TimeSpan.FromSeconds(baseSeconds) + TimeSpan.FromMilliseconds(jitterMilliseconds);
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal sealed record ConversationFeedProjection(
        string OperationId,
        string Text,
        ToolAction? Action,
        ToolGrounding? Grounding,
        ToolGrounding[]? Groundings,
        ExternalAuthorizationContinuation? Authorization);
}

internal sealed class ConversationOperationLeaseUnavailableException()
    : InvalidOperationException("The conversation operation is already being processed.");
