using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Capabilities;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Kernel.Runtime;

[GrainType("digitalbrain.runtime.ino-operation-worker.v1")]
internal sealed class InoOperationWorkerGrain(
    IGrainFactory grainFactory,
    IAgentWorkflowRunner workflowRunner,
    IInoEffectExecutor effectExecutor,
    IEnumerable<IExternalAuthorizationResolver> authorizationResolvers,
    TimeProvider timeProvider,
    ILogger<InoOperationWorkerGrain> logger) : Grain, IInoOperationWorkerGrain, IRemindable
{
    private const string SafeFailure = "I couldn’t confirm the previous result. Review it before trying again.";
    private const string AuthorizationExpired = "Authorization wasn’t completed in time. Send the request again when you’re ready.";
    private const string AuthorizationFailed = "Authorization wasn’t completed. Reconnect and send the request again.";
    private const string ReminderName = "ino.operation-worker.execute.v1";
    private static readonly TimeSpan WorkerDeadline = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan AuthorizationProbeDeadline = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReminderDueTime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ReminderPeriod = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TimerInitialDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TimerRetryDelay = TimeSpan.FromSeconds(5);
    private const int MaximumWorkflowResultPersistenceAttempts = 8;
    private enum ResultPersistence { Persisted, Superseded, Contended }
    private static readonly ActivitySource ActivitySource = new("DigitalBrain.Ino.Worker");
    private readonly IReadOnlyDictionary<string, IExternalAuthorizationResolver> _authorizationResolvers =
        authorizationResolvers.ToDictionary(static resolver => resolver.Provider, StringComparer.Ordinal);
    private IGrainReminder? _reminder;
    private IGrainTimer? _timer;

    public async Task ScheduleAsync()
    {
        _reminder ??= await this.RegisterOrUpdateReminder(ReminderName, ReminderDueTime, ReminderPeriod);
        EnsureTimer(TimerInitialDelay);
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (!string.Equals(reminderName, ReminderName, StringComparison.Ordinal)) return;
        await ProcessScheduledAsync();
    }

    private async Task ReceiveTimerAsync(CancellationToken cancellationToken)
    {
        var timer = _timer;
        _timer = null;
        timer?.Dispose();
        await ProcessScheduledAsync();
    }

    private async Task ProcessScheduledAsync()
    {

        var (conversationGrainKey, operationId) = ParseWorkerKey(this.GetPrimaryKeyString() ?? throw new InvalidOperationException("Operation workers require a string key."));
        var dispatcher = grainFactory.GetGrain<IInoConversationOutboxDispatcherGrain>(conversationGrainKey);

        await dispatcher.ScheduleAsync();
        await ExecuteScheduledAsync(conversationGrainKey, operationId);

        var state = await grainFactory.GetGrain<IConversationNeuron>(conversationGrainKey).ReadAsync();
        if (state.Outbox.Any(entry => entry.DispatchedAt is null))
            await dispatcher.ScheduleAsync();
        var operation = state.Operations.FirstOrDefault(candidate =>
            string.Equals(candidate.OperationId, operationId, StringComparison.Ordinal));
        if (operation is not null && HasOperationToWatch(operation))
            EnsureTimer(TimerRetryDelay);
        else
            await StopReminderAsync();
    }

    private void EnsureTimer(TimeSpan dueTime) =>
        _timer ??= this.RegisterGrainTimer(ReceiveTimerAsync, new GrainTimerCreationOptions(dueTime, Timeout.InfiniteTimeSpan) { KeepAlive = true });

    private async Task ExecuteScheduledAsync(string conversationGrainKey, string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationGrainKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        var conversation = grainFactory.GetGrain<IConversationNeuron>(conversationGrainKey);
        var initial = await conversation.ReadAsync();
        var now = timeProvider.GetUtcNow();
        var operation = initial.Operations.FirstOrDefault(candidate =>
            string.Equals(candidate.OperationId, operationId, StringComparison.Ordinal));
        if (operation is null) return;

        var leaseOwner = "ino-worker-" + this.GetPrimaryKeyString();
        if (operation.Status == ConversationOperationStatus.AwaitingAuthorization ||
            operation.Status == ConversationOperationStatus.Running && operation.SuspendedInvocation is not null)
        {
            await ResumeAuthorizationAsync(conversation, conversationGrainKey, initial, operation, leaseOwner, now);
            return;
        }
        if (operation.Status == ConversationOperationStatus.Running)
        {
            if (operation.LeaseExpiresAt is { } leaseExpiry && leaseExpiry <= now)
            {
                ConversationClaim recovery;
                try
                {
                    recovery = await conversation.TryClaimOperationAsync(initial.Revision, operationId, leaseOwner, now, LeaseDuration, CreateRunningOutbox(initial, operation, now));
                }
                catch (RuntimeStateConflictException)
                {
                    return;
                }
                if (!recovery.Acquired || recovery.Operation is null) return;

                var leaseFence = LeaseFence(recovery.Operation);
                if (recovery.Operation.Effect?.State == "applying")
                    await PersistEffectResultAsync(
                        conversation,
                        recovery.Operation,
                        new InoToolEffectResult(InoToolEffectDisposition.OutcomeUnknown, "The previous external action could not be confirmed. Review it before trying again."),
                        activity: null);
                else
                    await ScheduleLeaseInterruptionRecoveryAsync(conversation, recovery.State, recovery.Operation, "INO is recovering an interrupted model run.", leaseFence);
            }
            return;
        }
        if (!IsEligible(operation, now)) return;

        ConversationClaim claim;
        try
        {
            claim = await conversation.TryClaimOperationAsync(initial.Revision, operationId, leaseOwner, now, LeaseDuration, CreateRunningOutbox(initial, operation, now));
        }
        catch (RuntimeStateConflictException)
        {
            return;
        }
        if (!claim.Acquired || claim.Operation is null) return;

        await ExecuteClaimedAsync(conversation, conversationGrainKey, claim.State, claim.Operation, authorizationResume: null);
    }

    private async Task ResumeAuthorizationAsync(
        IConversationNeuron conversation,
        string conversationGrainKey,
        ConversationState state,
        ConversationOperation operation,
        string leaseOwner,
        DateTimeOffset now)
    {
        var invocation = operation.SuspendedInvocation;
        if (invocation is null)
        {
            logger.LogError("INO authorization operation {OperationId} is missing its durable handoff.", operation.OperationId);
            return;
        }
        if (invocation.AuthorizationExpiresAt <= now)
        {
            var expiredClaim = await TryClaimAuthorizationAsync(conversation, state, operation, invocation, leaseOwner, now);
            if (expiredClaim is null || expiredClaim.Operation is null) return;
            await RecordAuthorizationFailureAsync(conversation, expiredClaim.State, expiredClaim.Operation, AuthorizationExpired, LeaseFence(expiredClaim.Operation));
            return;
        }
        if (state.Identity is null)
        {
            var identityClaim = await TryClaimAuthorizationAsync(conversation, state, operation, invocation, leaseOwner, now);
            if (identityClaim is null || identityClaim.Operation is null) return;
            await RecordUnknownAsync(conversation, identityClaim.State, identityClaim.Operation, SafeFailure, LeaseFence(identityClaim.Operation));
            return;
        }

        ExternalAuthorizationResolution resolution;
        try
        {
            using var probeDeadline = new CancellationTokenSource(AuthorizationProbeDeadline);
            resolution = await ResolveAuthorizationAsync(state.Identity, invocation.Provider, probeDeadline.Token);
        }
        catch (OperationCanceledException)
        {

            return;
        }
        catch (Exception)
        {
            logger.LogWarning("INO authorization readiness could not be confirmed for operation {OperationId}.", operation.OperationId);
            return;
        }
        if (resolution.State == ExternalAuthorizationResolutionState.Waiting) return;
        if (resolution.State != ExternalAuthorizationResolutionState.Ready)
        {
            var failedClaim = await TryClaimAuthorizationAsync(conversation, state, operation, invocation, leaseOwner, now);
            if (failedClaim is null || failedClaim.Operation is null) return;
            await RecordAuthorizationFailureAsync(conversation, failedClaim.State, failedClaim.Operation, AuthorizationFailed, LeaseFence(failedClaim.Operation));
            return;
        }

        var claim = await TryClaimAuthorizationAsync(conversation, state, operation, invocation, leaseOwner, now);
        if (claim is null || claim.Operation is null) return;

        await ExecuteClaimedAsync(
            conversation,
            conversationGrainKey,
            claim.State,
            claim.Operation,
            new InoAuthorizationResume(invocation.Provider, invocation.ToolId, invocation.AuthorizationAttemptId, invocation.AuthorizationExpiresAt));
    }

    private async Task ExecuteClaimedAsync(
        IConversationNeuron conversation,
        string conversationGrainKey,
        ConversationState state,
        ConversationOperation claimed,
        InoAuthorizationResume? authorizationResume)
    {
        var operationId = claimed.OperationId;

        using var activity = ActivitySource.StartActivity("ino.operation.execute", ActivityKind.Internal);
        activity?.SetTag("db.ino.operation_id", operationId);
        activity?.SetTag("db.ino.conversation_grain", conversationGrainKey);
        var requestId = string.IsNullOrWhiteSpace(claimed.RequestId) ? claimed.CommandId : claimed.RequestId;
        activity?.SetTag("db.ino.request_id", requestId);
        if (claimed.Effect?.State == "applying")
        {
            await ExecuteApprovedEffectAsync(conversation, state, claimed, activity);
            return;
        }
        var prompt = state.Turns.LastOrDefault(turn =>
            turn.Kind == ConversationTurnKind.User && string.Equals(turn.OperationId, operationId, StringComparison.Ordinal))?.Text;
        if (string.IsNullOrWhiteSpace(prompt) || state.Identity is null)
        {
            await RecordUnknownAsync(conversation, state, claimed, "The accepted request could not be recovered safely.", LeaseFence(claimed));
            return;
        }

        var history = state.Turns.Where(turn => !string.Equals(turn.OperationId, operationId, StringComparison.Ordinal))
            .TakeLast(12)
            .Select(turn => turn.Role + ": " + turn.Text)
            .ToArray();
        InoWorkflowResult result;
        try
        {
            using var deadline = new CancellationTokenSource(WorkerDeadline);
            result = await workflowRunner.ExecuteAsync(new(
                operationId,
                state.Identity.ConversationId,
                prompt,
                history,
                requestId,
                authorizationResume,
                claimed.Workflow,
                RequestScope.Id(state.Identity.OwnerId, state.Identity.ActorId),
                state.Identity.OwnerId,
                state.Identity.ActorId), deadline.Token);
        }
        catch (OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "deadline-exceeded");
            activity?.SetTag("db.ino.outcome", "failed");
            await RecordWorkflowFailureAsync(
                conversation,
                await conversation.ReadAsync(),
                claimed,
                "INO couldn’t complete this request before its model deadline. Send it again.",
                LeaseFence(claimed));
            return;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "workflow-failed");
            activity?.SetTag("db.ino.outcome", "failed");
            logger.LogWarning("INO model workflow {OperationId} failed with {ExceptionType} before an external effect began.", operationId, ex.GetType().Name);
            await RecordWorkflowFailureAsync(conversation, await conversation.ReadAsync(), claimed, "INO couldn’t complete this request. Send it again.", LeaseFence(claimed));
            return;
        }

        activity?.SetTag("db.ino.workflow_id", result.Workflow.WorkflowId);
        activity?.SetTag("db.ino.workflow_session_id", result.Workflow.SessionId);
        if (result.ToolRequest is { } requestedTool)
            activity?.SetTag("db.ino.tool_id", requestedTool.ToolId);
        await PersistWorkflowResultAsync(conversation, state.Identity, claimed, result, activity);
    }

    private async Task PersistWorkflowResultAsync(IConversationNeuron conversation, ConversationIdentity identity, ConversationOperation claimed, InoWorkflowResult result, Activity? activity)
    {
        var leaseFence = LeaseFence(claimed);
        var requestedTool = result.ToolRequest;
        InoApprovedTool? approvedTool = null;
        if (requestedTool is { Access: InoToolAccess.Mutation })
        {
            var actorScope = RequestScope.Id(identity.OwnerId, identity.ActorId);
            if (effectExecutor.TryAuthorizeMutation(requestedTool, actorScope, out var authorized))
                approvedTool = authorized;
        }

        for (var attempt = 0; attempt < MaximumWorkflowResultPersistenceAttempts; attempt++)
        {
            var state = await conversation.ReadAsync();
            if (LeaseOwnedRunningOperation(state, claimed.OperationId, leaseFence) is not { } current)
            {
                activity?.SetTag("db.ino.outcome", "superseded");
                return;
            }

            try
            {
                var outcome = await PersistWorkflowResultTransitionAsync(conversation, state, current, leaseFence, result, requestedTool, approvedTool);
                activity?.SetTag("db.ino.outcome", outcome);
                return;
            }
            catch (RuntimeStateConflictException)
            {

            }
            catch (ArgumentException)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "workflow-result-invalid");
                await PersistWorkflowResultOutcomeUnknownAsync(conversation, claimed, result.Workflow, leaseFence, activity);
                return;
            }
        }

        await PersistWorkflowResultOutcomeUnknownAsync(conversation, claimed, result.Workflow, leaseFence, activity);
    }

    private async Task<string> PersistWorkflowResultTransitionAsync(
        IConversationNeuron conversation,
        ConversationState state,
        ConversationOperation current,
        ConversationLeaseFence leaseFence,
        InoWorkflowResult result,
        InoToolRequest? requestedTool,
        InoApprovedTool? approvedTool)
    {
        var now = timeProvider.GetUtcNow();
        if (result.AuthorizationRequest is { } authorization)
        {
            var summary = BoundedSafeText(authorization.SafeSummary, "Connect the required account to continue.");
            var invocation = new SuspendedInvocation(
                authorization.Provider,
                authorization.ToolId,
                [],
                authorization.AuthorizationAttemptId,
                authorization.ExpiresAt,
                authorization.AuthorizationFlowReference,
                result.Workflow);
            await conversation.SuspendAuthorizationWithAssistantAsync(
                state.Revision,
                current.OperationId,
                invocation,
                summary,
                CreateOutbox(
                    state,
                    current,
                    current.OperationId,
                    InoOperationPhase.AwaitingAuthorization,
                    checked(current.Version + 1),
                    summary,
                    now,
                    workflow: result.Workflow,
                    action: new ToolAction(
                        "openUrl",
                        authorization.Provider == OAuthCallbackPaths.GoogleProvider ? "Connect Google" : "Connect Salesforce",
                        OAuthCallbackPaths.CreateInternalStartPath(authorization.Provider, authorization.AuthorizationFlowReference)),
                    toolId: authorization.ToolId),
                now,
                leaseFence);
            return "awaiting-authorization";
        }

        if (requestedTool is { Access: InoToolAccess.Mutation })
        {
            if (approvedTool is null)
            {
                const string safeReason = "This requested tool is not authorized for the current workspace. No external action was performed.";
                await CompleteWorkflowResultAsync(
                    conversation,
                    state,
                    current,
                    ConversationOperationStatus.Failed,
                    ConversationTerminalPolicy.NeverRetry,
                    safeReason,
                    safeReason,
                    result.Workflow,
                    leaseFence,
                    now);
                return "failed";
            }

            var effectId = StableIdentifier("effect", current.OperationId, approvedTool.ToolId, approvedTool.Scope);
            var approvalId = StableIdentifier("approval", current.OperationId, effectId);
            var summary = string.IsNullOrWhiteSpace(approvedTool.SafeSummary) ? "a typed workspace change" : approvedTool.SafeSummary.Trim();
            if (summary.Length > 512) summary = summary[..512];
            var text = "Approval is required before INO can perform " + summary + ".";
            var approval = new ApprovalRecord(approvalId, current.OperationId, effectId, "requested", 1, now);
            var effect = new EffectRecord(effectId, current.OperationId, approvedTool.ToolId, approvedTool.Scope, "awaiting-approval", effectId, 1);
            await conversation.RequestApprovalWithAssistantAsync(
                state.Revision,
                current.OperationId,
                approval,
                effect,
                text,
                CreateOutbox(
                    state,
                    current,
                    current.OperationId,
                    InoOperationPhase.AwaitingApproval,
                    checked(current.Version + 1),
                    text,
                    now,
                    workflow: result.Workflow,
                    toolId: approvedTool.ToolId,
                    effectId: effectId,
                    approvalId: approvalId),
                now,
                result.Workflow,
                leaseFence);
            return "awaiting-approval";
        }

        if (requestedTool is not null)
        {
            const string safeReason = "This request needs a configured typed tool or authorization handoff. No external action was performed.";
            await CompleteWorkflowResultAsync(
                conversation,
                state,
                current,
                ConversationOperationStatus.Failed,
                ConversationTerminalPolicy.NeverRetry,
                safeReason,
                safeReason,
                result.Workflow,
                leaseFence,
                now,
                requestedTool.ToolId);
            return "failed";
        }

        await CompleteWorkflowResultAsync(
            conversation,
            state,
            current,
            ConversationOperationStatus.Succeeded,
            ConversationTerminalPolicy.NeverRetry,
            null,
            result.Text,
            result.Workflow,
            leaseFence,
            now);
        return "succeeded";
    }

    private async Task CompleteWorkflowResultAsync(
        IConversationNeuron conversation,
        ConversationState state,
        ConversationOperation current,
        ConversationOperationStatus terminalStatus,
        ConversationTerminalPolicy terminalPolicy,
        string? safeReason,
        string assistantText,
        WorkflowReference workflow,
        ConversationLeaseFence leaseFence,
        DateTimeOffset now,
        string? toolId = null) =>
        await conversation.CompleteWithAssistantAsync(
            state.Revision,
            current.OperationId,
            terminalStatus,
            terminalPolicy,
            safeReason,
            assistantText,
            CreateOutbox(
                state,
                current,
                current.OperationId,
                terminalStatus switch
                {
                    ConversationOperationStatus.Succeeded => InoOperationPhase.Succeeded,
                    ConversationOperationStatus.Failed => InoOperationPhase.Failed,
                    _ => InoOperationPhase.OutcomeUnknown
                },
                checked(current.Version + 1),
                assistantText,
                now,
                workflow: workflow,
                toolId: toolId),
            now,
            workflow,
            leaseFence);

    private async Task PersistWorkflowResultOutcomeUnknownAsync(IConversationNeuron conversation, ConversationOperation claimed, WorkflowReference workflow, ConversationLeaseFence leaseFence, Activity? activity)
    {
        for (var attempt = 0; attempt < MaximumWorkflowResultPersistenceAttempts; attempt++)
        {
            var state = await conversation.ReadAsync();
            if (LeaseOwnedRunningOperation(state, claimed.OperationId, leaseFence) is not { } current)
            {
                activity?.SetTag("db.ino.outcome", "superseded");
                return;
            }

            var now = timeProvider.GetUtcNow();
            try
            {
                await CompleteWorkflowResultAsync(
                    conversation,
                    state,
                    current,
                    ConversationOperationStatus.OutcomeUnknown,
                    ConversationTerminalPolicy.VerifyBeforeRetry,
                    SafeFailure,
                    SafeFailure,
                    workflow,
                    leaseFence,
                    now);
                activity?.SetTag("db.ino.outcome", "outcome-unknown");
                return;
            }
            catch (RuntimeStateConflictException)
            {

            }
        }

        activity?.SetStatus(ActivityStatusCode.Error, "workflow-result-unrecorded");
        activity?.SetTag("db.ino.outcome", "outcome-unknown");
        logger.LogWarning("INO workflow result for operation {OperationId} could not be recorded after bounded reconciliation.", claimed.OperationId);
    }

    private static ConversationOperation? LeaseOwnedRunningOperation(ConversationState state, string operationId, ConversationLeaseFence leaseFence) => state.Operations.FirstOrDefault(candidate =>
            string.Equals(candidate.OperationId, operationId, StringComparison.Ordinal) &&
            candidate.Status == ConversationOperationStatus.Running &&
            string.Equals(candidate.LeaseOwner, leaseFence.LeaseOwner, StringComparison.Ordinal) &&
            candidate.Attempt == leaseFence.Attempt);

    private Task<ExternalAuthorizationResolution> ResolveAuthorizationAsync(ConversationIdentity identity, string provider, CancellationToken cancellationToken)
    {
        return _authorizationResolvers.TryGetValue(provider, out var resolver)
            ? resolver.ResolveAsync(identity.OwnerId, identity.ActorId, cancellationToken)
            : Task.FromResult(new ExternalAuthorizationResolution(ExternalAuthorizationResolutionState.Failed, "authorization-provider-unsupported"));
    }

    private async Task<ConversationClaim?> TryClaimAuthorizationAsync(
        IConversationNeuron conversation,
        ConversationState state,
        ConversationOperation operation,
        SuspendedInvocation invocation,
        string leaseOwner,
        DateTimeOffset now)
    {
        try
        {
            var claim = await conversation.TryClaimAuthorizationAsync(state.Revision, operation.OperationId, invocation.AuthorizationAttemptId, leaseOwner, now, LeaseDuration, CreateRunningOutbox(state, operation, now));
            return claim.Acquired && claim.Operation is not null ? claim : null;
        }
        catch (RuntimeStateConflictException)
        {
            return null;
        }
    }

    private async Task RecordAuthorizationFailureAsync(IConversationNeuron conversation, ConversationState state, ConversationOperation operation, string safeReason, ConversationLeaseFence leaseFence)
    {
        var current = state.Operations.FirstOrDefault(candidate =>
            string.Equals(candidate.OperationId, operation.OperationId, StringComparison.Ordinal));
        if (current is null || IsTerminal(current.Status)) return;
        var now = timeProvider.GetUtcNow();
        try
        {
            await conversation.CompleteWithAssistantAsync(
                state.Revision,
                current.OperationId,
                ConversationOperationStatus.Failed,
                ConversationTerminalPolicy.NeverRetry,
                safeReason,
                safeReason,
                CreateOutbox(state, current, current.OperationId, InoOperationPhase.Failed, checked(current.Version + 1), safeReason, now),
                now,
                workflow: null,
                leaseFence: leaseFence);
        }
        catch (RuntimeStateConflictException)
        {
            return;
        }
    }

    private async Task ExecuteApprovedEffectAsync(IConversationNeuron conversation, ConversationState state, ConversationOperation claimed, Activity? activity)
    {
        var effect = claimed.Effect;
        if (effect is null || effect.State != "applying" || state.Identity is null)
        {
            await RecordEffectFailureAsync(conversation, state, claimed, "The approved action could not be prepared safely. No external action was performed.", LeaseFence(claimed));
            return;
        }

        var actorScope = RequestScope.Id(state.Identity.OwnerId, state.Identity.ActorId);
        activity?.SetTag("db.ino.tool_id", effect.Kind);
        activity?.SetTag("db.ino.effect_id", effect.EffectId);
        InoToolEffectResult result;
        try
        {
            using var deadline = new CancellationTokenSource(WorkerDeadline);
            result = await effectExecutor.ExecuteAsync(new InoToolEffectRequest(claimed.OperationId, effect.EffectId, effect.Kind, effect.Scope, actorScope, effect.ProviderIdempotencyKey), deadline.Token);
        }
        catch (OperationCanceledException)
        {
            result = new InoToolEffectResult(InoToolEffectDisposition.OutcomeUnknown, "The approved external action timed out before its result could be confirmed.");
        }
        catch (Exception)
        {
            logger.LogWarning("INO effect {EffectId} reached an uncertain outcome.", effect.EffectId);
            result = new InoToolEffectResult(InoToolEffectDisposition.OutcomeUnknown, "The approved external action could not be confirmed. Review it before trying again.");
        }

        await PersistEffectResultAsync(conversation, claimed, result, activity);
    }

    private Task RecordEffectFailureAsync(
        IConversationNeuron conversation,
        ConversationState state,
        ConversationOperation claimed,
        string safeReason,
        ConversationLeaseFence? leaseFence = null) =>
        CompleteEffectAsync(
            conversation,
            state,
            claimed,
            "failed",
            ConversationOperationStatus.Failed,
            ConversationTerminalPolicy.NeverRetry,
            safeReason,
            safeReason,
            leaseFence);

    private async Task CompleteEffectAsync(
        IConversationNeuron conversation,
        ConversationState state,
        ConversationOperation claimed,
        string effectState,
        ConversationOperationStatus terminalStatus,
        ConversationTerminalPolicy terminalPolicy,
        string? safeReason,
        string assistantText,
        ConversationLeaseFence? leaseFence = null)
    {
        var current = state.Operations.FirstOrDefault(candidate =>
            string.Equals(candidate.OperationId, claimed.OperationId, StringComparison.Ordinal));
        if (current?.Effect is not { State: "applying" } effect || IsTerminal(current.Status)) return;
        var now = timeProvider.GetUtcNow();
        var resolvedEffect = effect with { State = effectState, Version = checked(effect.Version + 1) };
        try
        {
            await conversation.CompleteEffectWithAssistantAsync(
                state.Revision,
                current.OperationId,
                resolvedEffect,
                terminalStatus,
                terminalPolicy,
                safeReason,
                BoundedSafeText(assistantText, "The approved action has finished."),
                CreateOutbox(
                    state,
                    current,
                    current.OperationId,
                    terminalStatus switch
                    {
                        ConversationOperationStatus.Succeeded => InoOperationPhase.Succeeded,
                        ConversationOperationStatus.Failed => InoOperationPhase.Failed,
                        _ => InoOperationPhase.OutcomeUnknown
                    },
                    checked(current.Version + 1),
                    BoundedSafeText(assistantText, "The approved action has finished."),
                    now,
                    toolId: effect.Kind,
                    effectId: effect.EffectId),
                now,
                leaseFence);
        }
        catch (RuntimeStateConflictException)
        {

        }
    }

    private async Task PersistEffectResultAsync(IConversationNeuron conversation, ConversationOperation claimed, InoToolEffectResult result, Activity? activity)
    {
        var persistence = await TryPersistEffectResultAsync(conversation, claimed, result);
        if (persistence == ResultPersistence.Persisted)
        {
            activity?.SetTag("db.ino.outcome", EffectOutcome(result.Disposition));
            return;
        }
        if (persistence == ResultPersistence.Superseded)
        {
            activity?.SetTag("db.ino.outcome", "superseded");
            return;
        }

        var unknown = new InoToolEffectResult(InoToolEffectDisposition.OutcomeUnknown, "The approved external action could not be recorded safely. Review it before trying again.");
        persistence = await TryPersistEffectResultAsync(conversation, claimed, unknown);
        if (persistence == ResultPersistence.Persisted)
        {
            activity?.SetTag("db.ino.outcome", "outcome-unknown");
            return;
        }
        if (persistence == ResultPersistence.Superseded)
        {
            activity?.SetTag("db.ino.outcome", "superseded");
            return;
        }

        activity?.SetStatus(ActivityStatusCode.Error, "effect-result-unrecorded");
        activity?.SetTag("db.ino.outcome", "outcome-unknown");
        logger.LogWarning("INO effect result for operation {OperationId} could not be recorded after bounded reconciliation.", claimed.OperationId);
    }

    private async Task<ResultPersistence> TryPersistEffectResultAsync(IConversationNeuron conversation, ConversationOperation claimed, InoToolEffectResult result)
    {
        var leaseFence = LeaseFence(claimed);
        var terminalStatus = result.Disposition switch
        {
            InoToolEffectDisposition.Succeeded => ConversationOperationStatus.Succeeded,
            InoToolEffectDisposition.Failed => ConversationOperationStatus.Failed,
            _ => ConversationOperationStatus.OutcomeUnknown
        };
        var effectState = result.Disposition switch
        {
            InoToolEffectDisposition.Succeeded => "succeeded",
            InoToolEffectDisposition.Failed => "failed",
            _ => "outcome-unknown"
        };
        var safeResult = BoundedSafeText(result.SafeResult, "The approved action completed.");

        for (var attempt = 0; attempt < MaximumWorkflowResultPersistenceAttempts; attempt++)
        {
            var state = await conversation.ReadAsync();
            if (LeaseOwnedApplyingEffect(state, claimed.OperationId, leaseFence) is not { } current)
                return ResultPersistence.Superseded;

            var effect = current.Effect!;
            var now = timeProvider.GetUtcNow();
            var resolvedEffect = effect with { State = effectState, Version = checked(effect.Version + 1) };
            try
            {
                await conversation.CompleteEffectWithAssistantAsync(
                    state.Revision,
                    current.OperationId,
                    resolvedEffect,
                    terminalStatus,
                    terminalStatus == ConversationOperationStatus.OutcomeUnknown
                        ? ConversationTerminalPolicy.VerifyBeforeRetry
                        : ConversationTerminalPolicy.NeverRetry,
                    terminalStatus == ConversationOperationStatus.Succeeded ? null : safeResult,
                    safeResult,
                    CreateOutbox(
                        state,
                        current,
                        current.OperationId,
                        terminalStatus switch
                        {
                            ConversationOperationStatus.Succeeded => InoOperationPhase.Succeeded,
                            ConversationOperationStatus.Failed => InoOperationPhase.Failed,
                            _ => InoOperationPhase.OutcomeUnknown
                        },
                        checked(current.Version + 1),
                        safeResult,
                        now,
                        toolId: effect.Kind,
                        effectId: effect.EffectId),
                    now,
                    leaseFence);
                return ResultPersistence.Persisted;
            }
            catch (RuntimeStateConflictException)
            {

            }
        }

        return ResultPersistence.Contended;
    }

    private static ConversationOperation? LeaseOwnedApplyingEffect(ConversationState state, string operationId, ConversationLeaseFence leaseFence) => state.Operations.FirstOrDefault(candidate =>
            string.Equals(candidate.OperationId, operationId, StringComparison.Ordinal) &&
            candidate.Status == ConversationOperationStatus.Running &&
            candidate.Effect is { State: "applying" } &&
            string.Equals(candidate.LeaseOwner, leaseFence.LeaseOwner, StringComparison.Ordinal) &&
            candidate.Attempt == leaseFence.Attempt);

    private static string EffectOutcome(InoToolEffectDisposition disposition) => disposition switch
    {
        InoToolEffectDisposition.Succeeded => "succeeded",
        InoToolEffectDisposition.Failed => "failed",
        _ => "outcome-unknown"
    };

    private async Task ScheduleLeaseInterruptionRecoveryAsync(
        IConversationNeuron conversation,
        ConversationState state,
        ConversationOperation claimed,
        string safeReason,
        ConversationLeaseFence? leaseFence = null)
    {
        var current = state.Operations.FirstOrDefault(candidate =>
            string.Equals(candidate.OperationId, claimed.OperationId, StringComparison.Ordinal));
        if (current is null || IsTerminal(current.Status) || current.Effect is not null) return;
        if (current.Attempt >= 3)
        {
            await RecordWorkflowFailureAsync(conversation, state, claimed, "INO could not complete this request after safe retries. Please send it again.", leaseFence);
            return;
        }
        var now = timeProvider.GetUtcNow();
        var nextAttempt = now.AddSeconds(Math.Min(30, 1 << Math.Clamp(current.Attempt, 0, 5)));
        try
        {
            await conversation.ScheduleRetryAsync(
                state.Revision,
                current.OperationId,
                nextAttempt,
                BoundedSafeText(safeReason, "INO is retrying safely."),
                now,
                CreateOutbox(
                    state,
                    current,
                    current.OperationId,
                    InoOperationPhase.RetryScheduled,
                    checked(current.Version + 1),
                    BoundedSafeText(safeReason, "INO is retrying safely."),
                    now),
                leaseFence);
        }
        catch (RuntimeStateConflictException)
        {

        }
    }

    private async Task RecordWorkflowFailureAsync(
        IConversationNeuron conversation,
        ConversationState state,
        ConversationOperation claimed,
        string safeReason,
        ConversationLeaseFence? leaseFence = null)
    {
        var text = BoundedSafeText(safeReason, "INO could not complete this request.");
        var fence = leaseFence ?? LeaseFence(claimed);
        for (var attempt = 0; attempt < MaximumWorkflowResultPersistenceAttempts; attempt++)
        {
            var currentState = attempt == 0 ? state : await conversation.ReadAsync();
            var current = LeaseOwnedRunningOperation(currentState, claimed.OperationId, fence);
            if (current is null || current.Effect is not null) return;
            var now = timeProvider.GetUtcNow();
            try
            {
                await conversation.CompleteWithAssistantAsync(
                    currentState.Revision,
                    current.OperationId,
                    ConversationOperationStatus.Failed,
                    ConversationTerminalPolicy.NeverRetry,
                    text,
                    text,
                    CreateOutbox(currentState, current, current.OperationId, InoOperationPhase.Failed, checked(current.Version + 1), text, now),
                    now,
                    workflow: null,
                    leaseFence: fence);
                return;
            }
            catch (RuntimeStateConflictException)
            {

            }
        }

        logger.LogWarning("INO workflow failure for operation {OperationId} could not be recorded after bounded reconciliation.", claimed.OperationId);
    }

    private async Task RecordUnknownAsync(
        IConversationNeuron conversation,
        ConversationState state,
        ConversationOperation claimed,
        string safeReason,
        ConversationLeaseFence? leaseFence = null)
    {
        var current = state.Operations.FirstOrDefault(candidate =>
            string.Equals(candidate.OperationId, claimed.OperationId, StringComparison.Ordinal));
        if (current is null || IsTerminal(current.Status)) return;
        var now = timeProvider.GetUtcNow();
        try
        {
            await conversation.CompleteWithAssistantAsync(
                state.Revision,
                current.OperationId,
                ConversationOperationStatus.OutcomeUnknown,
                ConversationTerminalPolicy.VerifyBeforeRetry,
                safeReason,
                safeReason,
                CreateOutbox(state, current, current.OperationId, InoOperationPhase.OutcomeUnknown, checked(current.Version + 1), safeReason, now),
                now,
                workflow: null,
                leaseFence: leaseFence);
        }
        catch (RuntimeStateConflictException)
        {

        }
    }

    private static ConversationOutboxEntry CreateOutbox(
        ConversationState state,
        ConversationOperation operation,
        string operationId,
        InoOperationPhase phase,
        long version,
        string text,
        DateTimeOffset now,
        WorkflowReference? workflow = null,
        ToolAction? action = null,
        string? toolId = null,
        string? effectId = null,
        string? approvalId = null)
    {
        var identity = state.Identity ?? throw new RuntimeStateIntegrityException("conversation identity is missing");
        var includeMessage = !string.IsNullOrWhiteSpace(text) && phase is not InoOperationPhase.Running and not InoOperationPhase.RetryScheduled;
        var turns = state.Turns.Select(turn => new OperationFeedTurn(
            turn.IdempotencyKey,
            turn.Role,
            turn.Text,
            string.Equals(turn.OperationId, operationId, StringComparison.Ordinal)
                ? StateFor(phase)
                : state.Operations.FirstOrDefault(candidate =>
                    string.Equals(candidate.OperationId, turn.OperationId, StringComparison.Ordinal)) is { } turnOperation
                    ? StateFor(turnOperation.Status)
                    : InoConversationStates.Succeeded));
        if (includeMessage)
            turns = turns.Append(new OperationFeedTurn(operation.OperationId + ":" + phase.ToString().ToLowerInvariant() + ":" + version, "assistant", text, StateFor(phase)));
        var eventId = $"operation:{operationId}:phase:{phase.ToString().ToLowerInvariant()}:v:{version}";
        var record = OperationOutboxRecord.Create(
            eventId,
            operationId,
            phase,
            version,
            now,
            identity.ConversationId,
            checked(state.Revision + 1),
            string.IsNullOrWhiteSpace(operation.RequestId) ? operation.OperationId : operation.RequestId,
            RuntimeStateKeys.Conversation(identity.OwnerId, identity.ActorId, identity.ConversationId),
            new OperationFeedView(
                operation.CommandId,
                string.Empty,
                phase == InoOperationPhase.RetryScheduled,
                phase is InoOperationPhase.RetryScheduled or InoOperationPhase.Failed or InoOperationPhase.OutcomeUnknown ? text : null,
                approvalId ?? operation.Approval?.ApprovalId,
                action,
                turns.TakeLast(16).ToArray()),
            toolId,
            effectId,
            approvalId ?? operation.Approval?.ApprovalId,
            workflow ?? operation.Workflow);
        return new(eventId, "surface-feed", record.ToPayloadUtf8(), now, null);
    }

    private static ConversationOutboxEntry CreateRunningOutbox(ConversationState state, ConversationOperation operation, DateTimeOffset now)
    {
        var effect = operation.Effect;
        var phase = effect is { State: "approved" or "applying" } ? InoOperationPhase.ApplyingEffect : InoOperationPhase.Running;
        return CreateOutbox(state, operation, operation.OperationId, phase, checked(operation.Version + 1), string.Empty, now, toolId: effect?.Kind, effectId: effect?.EffectId);
    }

    private static ConversationOperation RequiredOperation(ConversationState state, string operationId) =>
        state.Operations.First(candidate => string.Equals(candidate.OperationId, operationId, StringComparison.Ordinal));

    private static string StateFor(ConversationOperationStatus status) => status switch
    {
        ConversationOperationStatus.Pending => InoConversationStates.Queued,
        ConversationOperationStatus.Running => InoConversationStates.Running,
        ConversationOperationStatus.AwaitingAuthorization => InoConversationStates.AwaitingAuthorization,
        ConversationOperationStatus.AwaitingApproval => InoConversationStates.AwaitingApproval,
        ConversationOperationStatus.RetryScheduled => InoConversationStates.RetryScheduled,
        ConversationOperationStatus.Succeeded => InoConversationStates.Succeeded,
        ConversationOperationStatus.OutcomeUnknown => InoConversationStates.OutcomeUnknown,
        ConversationOperationStatus.Cancelled => InoConversationStates.Cancelled,
        _ => InoConversationStates.Failed
    };

    private static string StateFor(InoOperationPhase phase) => phase switch
    {
        InoOperationPhase.Accepted or InoOperationPhase.Queued or InoOperationPhase.Approved => InoConversationStates.Queued,
        InoOperationPhase.Running or InoOperationPhase.ApplyingEffect => InoConversationStates.Running,
        InoOperationPhase.AwaitingAuthorization => InoConversationStates.AwaitingAuthorization,
        InoOperationPhase.AwaitingApproval => InoConversationStates.AwaitingApproval,
        InoOperationPhase.RetryScheduled => InoConversationStates.RetryScheduled,
        InoOperationPhase.Succeeded => InoConversationStates.Succeeded,
        InoOperationPhase.OutcomeUnknown => InoConversationStates.OutcomeUnknown,
        InoOperationPhase.Cancelled => InoConversationStates.Cancelled,
        _ => InoConversationStates.Failed
    };

    private static ConversationLeaseFence LeaseFence(ConversationOperation operation) =>
        new(operation.LeaseOwner ?? throw new InvalidOperationException("An executing operation requires a lease owner."), operation.Attempt);

    private static string StableIdentifier(string prefix, params string[] values)
    {
        var canonical = string.Join("\0", values);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return prefix + "-" + hash[..32];
    }

    private static string BoundedSafeText(string? value, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return text.Length <= 256 ? text : text[..256];
    }

    private static bool IsEligible(ConversationOperation operation, DateTimeOffset now) =>
        operation.Status == ConversationOperationStatus.Pending ||
        operation.Status == ConversationOperationStatus.RetryScheduled && operation.NextAttemptAt <= now;

    private static bool HasOperationToWatch(ConversationOperation operation) =>
        operation.Status is ConversationOperationStatus.Pending or
            ConversationOperationStatus.AwaitingAuthorization or
            ConversationOperationStatus.RetryScheduled or
            ConversationOperationStatus.Running;

    private static bool IsTerminal(ConversationOperationStatus status) => status is
        ConversationOperationStatus.Succeeded or ConversationOperationStatus.Failed or
        ConversationOperationStatus.OutcomeUnknown or ConversationOperationStatus.Cancelled;

    private async Task StopReminderAsync()
    {
        _timer?.Dispose();
        _timer = null;
        _reminder ??= await this.GetReminder(ReminderName);
        if (_reminder is null) return;
        await this.UnregisterReminder(_reminder);
        _reminder = null;
    }

    private static (string ConversationGrainKey, string OperationId) ParseWorkerKey(string workerKey)
    {
        if (workerKey.Length <= 65 || workerKey[64] != '|')
            throw new ArgumentException("Operation worker keys must include a conversation scope and operation id.", nameof(workerKey));

        var conversationGrainKey = workerKey[..64];
        var operationId = workerKey[65..];
        RuntimeStateKeys.DemandScopeHash(conversationGrainKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        return (conversationGrainKey, operationId);
    }
}
