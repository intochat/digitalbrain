using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Runtime;
using Orleans;
using RuntimeRequestContext = DigitalBrain.Kernel.Contracts.Runtime.RequestContext;
namespace DigitalBrain.Mcp;

public sealed record RuntimeFeedPage(IReadOnlyList<StoredSurfaceRecord> Items, bool ResetRequired, long LatestSequence);
public sealed record PreparedRuntimeFeed(SurfaceFeedState State, IReadOnlyDictionary<string, SurfaceActionToken> ActionTokens);
public sealed record AuthorizedRuntimeAction(ActionSubmission Submission, SurfaceActionBinding? Binding, string ConversationId);
internal sealed record ApprovalDecisionInput(string OperationId, string ApprovalId, bool Approved, string ClientDecisionId);
internal sealed record FeatureReleaseDecisionInput(string ApprovalId, string ReleaseDigest, long ExpectedRevision, bool Approved, string ClientDecisionId);
internal static class FeatureApprovalSurface
{
    public const string SurfaceId = "surface.feature-approval";
    public const string ActionType = "feature.release.decision.v1";
    public const string InputSchema = "digitalbrain.feature.release-decision.v1";
    public static readonly string[] RequiredCapabilities =
        ["ui.protocol.v2", "ui.payload.native", "ui.native.feature-approval", "ui.native.typed-actions"];
}
public sealed class RuntimeSurfaceFeed(IClusterClient cluster, TimeProvider timeProvider, SessionTokenService actionCapabilities)
{
    private static readonly TimeSpan CursorLifetime = TimeSpan.FromDays(30);
    public async Task<string> ResolveActiveConversationIdAsync(RuntimeRequestContext context, CancellationToken cancellationToken)
    {
        DemandActor(context);
        var neuron = Feed(context);
        var state = await EnsureInitializedAsync(context, neuron, cancellationToken).ConfigureAwait(false);
        return ActiveConversationId(context, state);
    }
    public async Task<PreparedRuntimeFeed> PrepareSessionAsync(RuntimeRequestContext context, CancellationToken cancellationToken = default)
    {
        DemandActor(context);
        var neuron = Feed(context);
        var state = await EnsureInitializedAsync(context, neuron, cancellationToken).ConfigureAwait(false);
        if (state.CurrentSurfaces.Length == 0)
            state = await EnsureHomeSurfaceAsync(context, neuron, state, cancellationToken).ConfigureAwait(false);
        state = await RenewActionBindingsAsync(neuron, state, cancellationToken).ConfigureAwait(false);
        var conversationId = ActiveConversationId(context, state);
        var conversationGrainKey = RuntimeStateKeys.Conversation(context.OwnerId, context.ActorId, conversationId);
        var conversationState = await cluster.GetGrain<IConversationNeuron>(conversationGrainKey).ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (conversationState.Outbox.Any(entry => entry.DispatchedAt is null))
            await cluster.GetGrain<IInoConversationOutboxDispatcherGrain>(conversationGrainKey).ScheduleAsync().WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        return new(state, IssueActionCapabilities(context, state));
    }
    public async Task PublishFeatureApprovalAsync(RuntimeRequestContext context, FeatureApprovalSnapshot approval, CancellationToken cancellationToken = default)
    {
        DemandActor(context);
        var feed = Feed(context);
        var state = await EnsureInitializedAsync(context, feed, cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(UiProtocol.ActionTokenLifetime);
        var payload = JsonSerializer.SerializeToElement(new
        {
            kind = "native",
            nativeKind = "featureApproval",
            data = new
            {
                title = "Approve Feature release",
                installationId = approval.InstallationId.Value,
                approvalId = approval.ApprovalId,
                releaseDigest = approval.Release.Digest.Value,
                sourceReference = approval.Release.SourceReference,
                sourceKind = approval.Release.SourceKind.ToString(),
                requestedCapabilities = approval.Release.RequestedCapabilities,
                addedCapabilities = approval.AddedCapabilities,
                removedCapabilities = approval.RemovedCapabilities,
                capabilityBindings = approval.Grants.Select(grant => new
                {
                    capabilityId = grant.CapabilityId,
                    capabilityVersion = grant.CapabilityVersion,
                    provider = grant.Provider,
                    providerConnectionId = grant.ProviderConnectionId?.Value,
                    constraints = ParseJson(grant.ConstraintsJson)
                }).ToArray(),
                revision = approval.Revision
            }
        });
        var bindingId = "feature-approval-" + approval.ApprovalId;
        var descriptor = new StoredActionBinding(bindingId, FeatureApprovalSurface.ActionType, FeatureApprovalSurface.InputSchema, "feature.manage", 1, expiresAt);
        var revision = checked((state.CurrentSurfaces.FirstOrDefault(surface =>
            string.Equals(surface.SurfaceId, FeatureApprovalSurface.SurfaceId, StringComparison.Ordinal))?.SurfaceRevision ?? 0) + 1);
        var presentation = new SurfaceFeedPresentation(
            context.CorrelationId,
            "feature-approval",
            approval.ApprovalId,
            FeatureApprovalSurface.RequiredCapabilities,
            payload,
            0,
            SurfaceFeedPresentation.CurrentVersion);
        var projection = new SurfaceFeedProjection(
            "feature-approval-" + approval.ApprovalId + "-" + approval.Revision,
            FeatureApprovalSurface.SurfaceId,
            revision,
            SurfaceContentHash.Compute(payload, [descriptor]),
            JsonSerializer.SerializeToUtf8Bytes(presentation),
            now,
            expiresAt,
            [new SurfaceActionBinding(
                bindingId,
                FeatureApprovalSurface.SurfaceId,
                revision,
                FeatureApprovalSurface.ActionType,
                FeatureApprovalSurface.InputSchema,
                "feature.manage",
                UiProtocol.ActionSchemaVersion,
                Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)),
                1,
                0,
                expiresAt,
                null,
                null)]);
        await feed.ApplyProjectionAsync(state.Revision, projection, now).WaitAsync(cancellationToken).ConfigureAwait(false);
    }
    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
    public async Task RestoreConversationSurfaceAsync(RuntimeRequestContext context, CancellationToken cancellationToken = default)
    {
        DemandActor(context);
        var feed = Feed(context);
        var state = await feed.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var conversationId = ActiveConversationId(context, state);
        var now = timeProvider.GetUtcNow();
        state = await feed.RebuildAsync(state.Revision, "feature-approval-return-" + Guid.NewGuid().ToString("N"), now).WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        await EnsureHomeSurfaceAsync(context with { ConversationId = conversationId }, feed, state, cancellationToken).ConfigureAwait(false);
    }
    public RuntimeFeedPage ReadPage(RuntimeRequestContext context, SurfaceFeedState state, long afterSequence, int limit)
    {
        DemandActor(context);
        if (afterSequence < 0) throw new ArgumentOutOfRangeException(nameof(afterSequence));
        var bounded = Math.Clamp(limit, 1, 100);
        var history = state.EventHistory ?? [];
        var oldestRetained = history.Length == 0 ? (long?)null : history[0].Sequence;
        var reset = afterSequence > state.LastSequence ||
                    afterSequence < state.LastSequence && (oldestRetained is null || afterSequence < oldestRetained.Value - 1);
        var items = reset
            ? state.CurrentSurfaces.OrderBy(surface => surface.Sequence)
                .TakeLast(bounded)
                .Select(surface => ToStoredRecord(context, state, surface, includeActions: true))
                .ToArray()
            : history.Where(surface => surface.Sequence > afterSequence)
            .OrderBy(surface => surface.Sequence)
            .Take(bounded)
            .Select(surface => ToStoredRecord(
                context,
                state,
                surface,
                includeActions: state.CurrentSurfaces.Any(current =>
                    string.Equals(current.SurfaceId, surface.SurfaceId, StringComparison.Ordinal) &&
                    current.SurfaceRevision == surface.SurfaceRevision && current.Sequence == surface.Sequence)))
            .ToArray();
        return new(items, reset, state.LastSequence);
    }
    public async Task<SurfaceFeedState> ReadAsync(RuntimeRequestContext context, CancellationToken cancellationToken = default) =>
        await Feed(context).ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
    public async Task RecordDeliveredAsync(RuntimeRequestContext context, long sequence, CancellationToken cancellationToken = default)
    {
        var neuron = Feed(context);
        var state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var deliveryId = DeliveryId(context.SessionId.Value, sequence);
        await RetryConflictAsync(
            neuron,
            state,
            current => neuron.RecordDeliveryAsync(current.Revision, deliveryId, sequence, timeProvider.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);
    }
    public async Task<long> AcknowledgeAsync(RuntimeRequestContext context, long sequence, CancellationToken cancellationToken = default)
    {
        var neuron = Feed(context);
        var state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var deliveryId = DeliveryId(context.SessionId.Value, sequence);
        if (!state.DeliveryDedupe.Any(delivery =>
                string.Equals(delivery.DeliveryId, deliveryId, StringComparison.Ordinal) && delivery.Sequence == sequence))
            throw new InvalidOperationException("The feed sequence has not been delivered to this session.");
        var now = timeProvider.GetUtcNow();
        state = await RetryConflictAsync(
            neuron,
            state,
            current => neuron.AcknowledgeAsync(current.Revision, RuntimeStateKeys.Session(context.SessionId.Value), sequence, now.Add(CursorLifetime), now),
            cancellationToken).ConfigureAwait(false);
        return state.Acknowledgements.First(cursor =>
            string.Equals(cursor.SessionScopeHash, RuntimeStateKeys.Session(context.SessionId.Value), StringComparison.Ordinal)).Sequence;
    }
    public async Task<AuthorizedRuntimeAction> AuthorizeActionAsync(
        RuntimeRequestContext context,
        string bindingId,
        string actionToken,
        string surfaceId,
        int surfaceRevision,
        JsonElement input,
        CancellationToken cancellationToken = default)
    {
        DemandActor(context);
        var neuron = Feed(context);
        var state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var activeConversationId = ActiveConversationId(context, state);
        var hasApprovalDecision = TryReadApprovalDecision(input, out var approvalDecision);
        var hasFeatureDecision = TryReadFeatureReleaseDecision(input, out var featureDecision);
        if (TryReadPrompt(input, out var requestedPrompt, out var clientSubmissionId) && clientSubmissionId is not null)
        {
            if (!context.Grants.Contains("ui.action"))
                throw new ActionRejectedException(ActionRejection.PolicyDenied);
            var acceptedIdempotencyKey = StableIdempotencyKey(context, clientSubmissionId);
            var acceptedOperationId = ConversationStateClient.OperationId(context with { ConversationId = activeConversationId }, acceptedIdempotencyKey);
            var conversation = cluster.GetGrain<IConversationNeuron>(RuntimeStateKeys.Conversation(context.OwnerId, context.ActorId, activeConversationId));
            var conversationState = await conversation.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            var existing = conversationState.Operations.FirstOrDefault(candidate =>
                string.Equals(candidate.OperationId, acceptedOperationId, StringComparison.Ordinal));
            if (existing is not null)
            {
                var priorPrompt = conversationState.Turns.LastOrDefault(turn =>
                    turn.Kind == ConversationTurnKind.User && string.Equals(turn.OperationId, acceptedOperationId, StringComparison.Ordinal))?.Text;
                if (!string.Equals(priorPrompt, requestedPrompt, StringComparison.Ordinal))
                    throw new ActionRejectedException(ActionRejection.Replay);
                return new(
                    new ActionSubmission(
                        acceptedOperationId,
                        acceptedIdempotencyKey,
                        JsonSerializer.SerializeToElement(new { prompt = requestedPrompt }),
                        ConversationSurfacePayload.SendActionType),
                    null,
                    activeConversationId);
            }
        }
        if (hasApprovalDecision)
        {
            if (!context.Grants.Contains("ui.action"))
                throw new ActionRejectedException(ActionRejection.PolicyDenied);
            var decisionId = StableIdempotencyKey(context, approvalDecision.ClientDecisionId);
            var conversation = cluster.GetGrain<IConversationNeuron>(RuntimeStateKeys.Conversation(context.OwnerId, context.ActorId, activeConversationId));
            var conversationState = await conversation.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            var target = conversationState.Operations.FirstOrDefault(candidate =>
                string.Equals(candidate.OperationId, approvalDecision.OperationId, StringComparison.Ordinal));
            if (target?.Approval is { DecisionId: not null } approval)
            {
                if (!string.Equals(approval.ApprovalId, approvalDecision.ApprovalId, StringComparison.Ordinal) ||
                    !string.Equals(approval.DecisionId, decisionId, StringComparison.Ordinal) ||
                    !string.Equals(approval.State, approvalDecision.Approved ? "approved" : "rejected", StringComparison.Ordinal))
                    throw new ActionRejectedException(ActionRejection.Replay);
                return new(
                    new ActionSubmission(target.OperationId, decisionId, CanonicalApprovalDecision(approvalDecision), ConversationSurfacePayload.ApprovalActionType),
                    null,
                    activeConversationId);
            }
        }
        var binding = DemandBinding(state, context, bindingId, surfaceId, surfaceRevision);
        DemandActionCapability(context, actionToken, binding);
        if (hasApprovalDecision)
            DemandApprovalMatchesBoundSurface(state, binding, approvalDecision);
        if (hasFeatureDecision)
            DemandFeatureDecisionMatchesBoundSurface(state, binding, featureDecision);
        var authorizedInput = AuthorizeInput(binding, input);
        var clientActionId = clientSubmissionId ??
            (hasApprovalDecision ? approvalDecision.ClientDecisionId : hasFeatureDecision ? featureDecision.ClientDecisionId : null);
        var idempotencyKey = clientActionId is null
            ? "surface-action-" + Hash(RequestScope.Id(context) + "\0" + bindingId + "\0" + surfaceRevision + "\0" + CanonicalJson(input))
            : StableIdempotencyKey(context, clientActionId);
        var operationId = hasApprovalDecision
            ? approvalDecision.OperationId
            : hasFeatureDecision
                ? "feature-" + Hash(RequestScope.Id(context) + "\0" + featureDecision.ApprovalId + "\0" + idempotencyKey)
                : ConversationStateClient.OperationId(context with { ConversationId = activeConversationId }, idempotencyKey);
        if (hasApprovalDecision)
            await DemandAwaitingApprovalAsync(context, activeConversationId, approvalDecision, cancellationToken).ConfigureAwait(false);
        SurfaceActionConsumption consumption;
        try
        {
            consumption = await neuron.ConsumeActionAsync(state.Revision, bindingId, binding.TokenHash, idempotencyKey, operationId, timeProvider.GetUtcNow()).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (RuntimeStateConflictException)
        {
            state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            binding = DemandBinding(state, context, bindingId, surfaceId, surfaceRevision);
            DemandActionCapability(context, actionToken, binding);
            if (hasApprovalDecision)
                DemandApprovalMatchesBoundSurface(state, binding, approvalDecision);
            if (hasFeatureDecision)
                DemandFeatureDecisionMatchesBoundSurface(state, binding, featureDecision);
            authorizedInput = AuthorizeInput(binding, input);
            activeConversationId = ActiveConversationId(context, state);
            operationId = hasApprovalDecision
                ? approvalDecision.OperationId
                : hasFeatureDecision
                    ? "feature-" + Hash(RequestScope.Id(context) + "\0" + featureDecision.ApprovalId + "\0" + idempotencyKey)
                    : ConversationStateClient.OperationId(context with { ConversationId = activeConversationId }, idempotencyKey);
            try
            {
                consumption = await neuron.ConsumeActionAsync(state.Revision, bindingId, binding.TokenHash, idempotencyKey, operationId, timeProvider.GetUtcNow()).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (RuntimeStateConflictException)
            {
                throw new ActionRejectedException(ActionRejection.WrongRevision);
            }
            catch (KeyNotFoundException)
            {
                throw new ActionRejectedException(ActionRejection.WrongRevision);
            }
            catch (UnauthorizedAccessException)
            {
                throw new ActionRejectedException(ActionRejection.WrongRevision);
            }
            catch (InvalidOperationException)
            {
                throw new ActionRejectedException(ActionRejection.Replay);
            }
        }
        catch (KeyNotFoundException)
        {
            throw new ActionRejectedException(ActionRejection.WrongRevision);
        }
        catch (UnauthorizedAccessException)
        {
            throw new ActionRejectedException(ActionRejection.WrongRevision);
        }
        catch (InvalidOperationException)
        {
            throw new ActionRejectedException(ActionRejection.Replay);
        }
        if (hasFeatureDecision && !consumption.Consumed)
            throw new ActionRejectedException(ActionRejection.Replay);
        return new(
            new ActionSubmission(consumption.OperationId, idempotencyKey, authorizedInput, consumption.AuthorizedBinding.ActionType),
            consumption.AuthorizedBinding,
            activeConversationId);
    }
    private SurfaceActionBinding DemandBinding(SurfaceFeedState state, RuntimeRequestContext context, string bindingId, string surfaceId, int surfaceRevision)
    {
        var surface = state.CurrentSurfaces.FirstOrDefault(candidate =>
            string.Equals(candidate.SurfaceId, surfaceId, StringComparison.Ordinal));
        if (surface is null || surface.SurfaceRevision != surfaceRevision)
            throw new ActionRejectedException(ActionRejection.WrongRevision);
        var binding = state.ActionBindings.FirstOrDefault(candidate =>
            string.Equals(candidate.BindingId, bindingId, StringComparison.Ordinal))
            ?? throw new ActionRejectedException(ActionRejection.WrongRevision);
        if (!string.Equals(binding.SurfaceId, surfaceId, StringComparison.Ordinal) || binding.SurfaceRevision != surfaceRevision || binding.ExpiresAt <= timeProvider.GetUtcNow())
            throw new ActionRejectedException(ActionRejection.WrongRevision);
        if (!context.Grants.Contains(binding.RequiredGrant))
            throw new ActionRejectedException(ActionRejection.PolicyDenied);
        return binding;
    }
    public async Task WaitForChangeAsync(RuntimeRequestContext context, long afterSequence, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await Feed(context).ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (state.LastSequence > afterSequence) return;
            await Task.Delay(TimeSpan.FromMilliseconds(250), timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }
    private async Task<SurfaceFeedState> EnsureInitializedAsync(RuntimeRequestContext context, ISurfaceFeedNeuron neuron, CancellationToken cancellationToken)
    {
        var state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var identity = new SurfaceFeedIdentity(context.OwnerId, context.ActorId);
        if (state.Identity is not null)
        {
            if (state.Identity != identity) throw new UnauthorizedAccessException("Surface-feed identity denied.");
            return state;
        }
        try
        {
            return await neuron.InitializeAsync(state.Revision, identity).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (RuntimeStateConflictException)
        {
            state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (state.Identity != identity) throw new UnauthorizedAccessException("Surface-feed identity denied.");
            return state;
        }
    }
    private async Task<SurfaceFeedState> EnsureHomeSurfaceAsync(RuntimeRequestContext context, ISurfaceFeedNeuron neuron, SurfaceFeedState initial, CancellationToken cancellationToken)
    {
        var state = initial;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (state.CurrentSurfaces.Length > 0) return state;
            var bootstrap = new HomeSurfaceBootstrap(
                "bootstrap-" + RequestScope.Id(context) + "-" + state.RebuildEpoch,
                InoConversationIdentity.From(context),
                context.CorrelationId,
                timeProvider.GetUtcNow());
            try
            {
                return await neuron.EnsureHomeSurfaceAsync(state.Revision, bootstrap).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (RuntimeStateConflictException) when (attempt < 2)
            {
                state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        throw new InvalidOperationException("Surface-feed home-surface revision retry exhausted.");
    }
    private ISurfaceFeedNeuron Feed(RuntimeRequestContext context) =>
        cluster.GetGrain<ISurfaceFeedNeuron>(RuntimeStateKeys.SurfaceFeed(context.OwnerId, context.ActorId));
    private async Task<SurfaceFeedState> RenewActionBindingsAsync(ISurfaceFeedNeuron neuron, SurfaceFeedState initial, CancellationToken cancellationToken)
    {
        var state = initial;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var now = timeProvider.GetUtcNow();
            try
            {
                return await neuron.RenewActionBindingsAsync(state.Revision, now).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (RuntimeStateConflictException) when (attempt < 2)
            {
                state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        throw new InvalidOperationException("Surface-feed action-binding renewal revision retry exhausted.");
    }
    private IReadOnlyDictionary<string, SurfaceActionToken> IssueActionCapabilities(RuntimeRequestContext context, SurfaceFeedState state)
    {
        var now = timeProvider.GetUtcNow();
        var activeSurfaces = state.CurrentSurfaces.Select(surface => (surface.SurfaceId, surface.SurfaceRevision))
            .ToHashSet();
        var issued = new Dictionary<string, SurfaceActionToken>(StringComparer.Ordinal);
        foreach (var binding in state.ActionBindings.Where(binding => binding.ExpiresAt > now && activeSurfaces.Contains((binding.SurfaceId, binding.SurfaceRevision)))
                     .OrderBy(binding => binding.BindingId, StringComparer.Ordinal))
        {
            if (!issued.TryAdd(
                    binding.BindingId,
                    new SurfaceActionToken(
                        actionCapabilities.IssueActionCapability(context, binding.BindingId, binding.SurfaceId, binding.SurfaceRevision, binding.TokenHash, binding.ExpiresAt),
                        binding.ExpiresAt)))
                throw new RuntimeStateIntegrityException("active action bindings must have unique binding identifiers");
        }
        return issued;
    }
    private void DemandActionCapability(RuntimeRequestContext context, string actionToken, SurfaceActionBinding binding)
    {
        if (!actionCapabilities.TryValidateActionCapability(actionToken, context, binding.BindingId, binding.SurfaceId, binding.SurfaceRevision, binding.TokenHash))
            throw new ActionRejectedException(ActionRejection.Forged);
    }
    private static StoredSurfaceRecord ToStoredRecord(RuntimeRequestContext context, SurfaceFeedState state, SurfaceFeedRecord surface, bool includeActions)
    {
        var presentation = ReadPresentation(surface);
        var actions = includeActions ? state.ActionBindings.Where(binding => string.Equals(binding.SurfaceId, surface.SurfaceId, StringComparison.Ordinal) &&
                              binding.SurfaceRevision == surface.SurfaceRevision)
            .Select(ToStoredBinding)
            .ToArray() : [];
        return new(
            surface.Sequence,
            context.OwnerId,
            context.ActorId,
            new SurfaceAudience(SurfaceAudienceKind.Actor, ActorScope.Id(context.ActorId)),
            surface.SurfaceId,
            surface.SurfaceRevision,
            surface.ContentHash,
            surface.CreatedAt,
            surface.ExpiresAt,
            presentation.CorrelationId,
            presentation.CauseKind,
            presentation.CauseId,
            presentation.RequiredClientCapabilities,
            presentation.Payload,
            actions);
    }
    private static StoredActionBinding ToStoredBinding(SurfaceActionBinding binding) => new(
        binding.BindingId,
        binding.ActionType,
        binding.InputSchemaRef,
        binding.RequiredGrant,
        binding.MaxUses,
        binding.ExpiresAt,
        binding.ActionSchemaVersion);
    private static string ActiveConversationId(RuntimeRequestContext context, SurfaceFeedState state)
    {
        var current = state.CurrentSurfaces.FirstOrDefault(surface =>
            string.Equals(surface.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal));
        if (current is null) return InoConversationIdentity.From(context);
        var presentation = ReadPresentation(current);
        if (string.Equals(presentation.CauseKind, "conversation", StringComparison.Ordinal) && IsCanonicalConversationId(presentation.CauseId))
            return presentation.CauseId;
        return InoConversationIdentity.From(context);
    }
    private static bool IsCanonicalConversationId(string? value)
    {
        if (value is null || value.Length != 68 || !value.StartsWith("ino-", StringComparison.Ordinal)) return false;
        foreach (var character in value.AsSpan(4))
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                return false;
        return true;
    }
    private static SurfaceFeedPresentation ReadPresentation(SurfaceFeedRecord surface)
    {
        try
        {
            return JsonSerializer.Deserialize<SurfaceFeedPresentation>(surface.PayloadUtf8)
                   ?? throw new RuntimeStateIntegrityException("empty surface presentation");
        }
        catch (JsonException)
        {
            throw new RuntimeStateIntegrityException("invalid surface presentation");
        }
    }
    private async Task DemandAwaitingApprovalAsync(RuntimeRequestContext context, string conversationId, ApprovalDecisionInput decision, CancellationToken cancellationToken)
    {
        var conversation = cluster.GetGrain<IConversationNeuron>(RuntimeStateKeys.Conversation(context.OwnerId, context.ActorId, conversationId));
        var state = await conversation.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var operation = state.Operations.FirstOrDefault(candidate =>
            string.Equals(candidate.OperationId, decision.OperationId, StringComparison.Ordinal));
        if (operation?.Status != ConversationOperationStatus.AwaitingApproval ||
            !string.Equals(operation.Approval?.ApprovalId, decision.ApprovalId, StringComparison.Ordinal) ||
            operation.Approval?.DecisionId is not null)
            throw new ActionRejectedException(ActionRejection.Unavailable);
    }
    private static void DemandApprovalMatchesBoundSurface(SurfaceFeedState state, SurfaceActionBinding binding, ApprovalDecisionInput decision)
    {
        if (!string.Equals(binding.ActionType, ConversationSurfacePayload.ApprovalActionType, StringComparison.Ordinal))
            throw new ActionRejectedException(ActionRejection.PolicyDenied);
        var surface = state.CurrentSurfaces.FirstOrDefault(candidate =>
            string.Equals(candidate.SurfaceId, binding.SurfaceId, StringComparison.Ordinal) &&
            candidate.SurfaceRevision == binding.SurfaceRevision)
            ?? throw new ActionRejectedException(ActionRejection.WrongRevision);
        var payload = ReadPresentation(surface).Payload;
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("operation", out var operation) || operation.ValueKind != JsonValueKind.Object ||
            !operation.TryGetProperty("operationId", out var operationId) || operationId.ValueKind != JsonValueKind.String ||
            !operation.TryGetProperty("approvalId", out var approvalId) || approvalId.ValueKind != JsonValueKind.String ||
            !string.Equals(operationId.GetString(), decision.OperationId, StringComparison.Ordinal) ||
            !string.Equals(approvalId.GetString(), decision.ApprovalId, StringComparison.Ordinal))
            throw new ActionRejectedException(ActionRejection.PolicyDenied);
    }
    private static void DemandFeatureDecisionMatchesBoundSurface(SurfaceFeedState state, SurfaceActionBinding binding, FeatureReleaseDecisionInput decision)
    {
        if (!string.Equals(binding.ActionType, FeatureApprovalSurface.ActionType, StringComparison.Ordinal))
            throw new ActionRejectedException(ActionRejection.PolicyDenied);
        var surface = state.CurrentSurfaces.FirstOrDefault(candidate =>
            string.Equals(candidate.SurfaceId, binding.SurfaceId, StringComparison.Ordinal) &&
            candidate.SurfaceRevision == binding.SurfaceRevision)
            ?? throw new ActionRejectedException(ActionRejection.WrongRevision);
        var payload = ReadPresentation(surface).Payload;
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("approvalId", out var approval) || approval.ValueKind != JsonValueKind.String ||
            !data.TryGetProperty("releaseDigest", out var release) || release.ValueKind != JsonValueKind.String ||
            !data.TryGetProperty("revision", out var revision) || !revision.TryGetInt64(out var expectedRevision) ||
            !string.Equals(approval.GetString(), decision.ApprovalId, StringComparison.Ordinal) ||
            !string.Equals(release.GetString(), decision.ReleaseDigest, StringComparison.Ordinal) ||
            expectedRevision != decision.ExpectedRevision)
            throw new ActionRejectedException(ActionRejection.PolicyDenied);
    }
    private static JsonElement AuthorizeInput(SurfaceActionBinding binding, JsonElement input)
    {
        if (binding.ActionSchemaVersion != UiProtocol.ActionSchemaVersion)
            throw new ActionRejectedException(ActionRejection.PolicyDenied);
        if (string.Equals(binding.ActionType, ConversationSurfacePayload.SendActionType, StringComparison.Ordinal) &&
            string.Equals(binding.InputSchemaRef, ConversationSurfacePayload.SendInputSchema, StringComparison.Ordinal) &&
            TryReadPrompt(input, out var prompt, out _))
            return JsonSerializer.SerializeToElement(new { prompt });
        if (string.Equals(binding.ActionType, ConversationSurfacePayload.ApprovalActionType, StringComparison.Ordinal) &&
            string.Equals(binding.InputSchemaRef, ConversationSurfacePayload.ApprovalInputSchema, StringComparison.Ordinal) &&
            TryReadApprovalDecision(input, out var decision))
            return CanonicalApprovalDecision(decision);
        if (string.Equals(binding.ActionType, FeatureApprovalSurface.ActionType, StringComparison.Ordinal) &&
            string.Equals(binding.InputSchemaRef, FeatureApprovalSurface.InputSchema, StringComparison.Ordinal) &&
            TryReadFeatureReleaseDecision(input, out var featureDecision))
            return CanonicalFeatureReleaseDecision(featureDecision);
        throw new ActionRejectedException(ActionRejection.PolicyDenied);
    }
    private static JsonElement CanonicalFeatureReleaseDecision(FeatureReleaseDecisionInput decision) =>
        JsonSerializer.SerializeToElement(new
        {
            approvalId = decision.ApprovalId,
            releaseDigest = decision.ReleaseDigest,
            expectedRevision = decision.ExpectedRevision,
            decision = decision.Approved ? "approve" : "reject",
            clientDecisionId = decision.ClientDecisionId
        });
    private static JsonElement CanonicalApprovalDecision(ApprovalDecisionInput decision) =>
        JsonSerializer.SerializeToElement(new
        {
            operationId = decision.OperationId,
            approvalId = decision.ApprovalId,
            decision = decision.Approved ? "approve" : "reject",
            clientDecisionId = decision.ClientDecisionId
        });
    private static bool TryReadPrompt(JsonElement input, out string prompt, out string? clientSubmissionId)
    {
        prompt = string.Empty;
        clientSubmissionId = null;
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty("prompt", out var value) || value.ValueKind != JsonValueKind.String)
            return false;
        var properties = input.EnumerateObject().ToArray();
        if (properties.Length is < 1 or > 2 || properties.Any(property =>
                !string.Equals(property.Name, "prompt", StringComparison.Ordinal) &&
                !string.Equals(property.Name, "clientSubmissionId", StringComparison.Ordinal)))
            return false;
        if (input.TryGetProperty("clientSubmissionId", out var submission))
        {
            if (submission.ValueKind != JsonValueKind.String) return false;
            clientSubmissionId = submission.GetString();
            if (string.IsNullOrWhiteSpace(clientSubmissionId) || clientSubmissionId.Length is < 16 or > 128 ||
                !clientSubmissionId.All(character =>
                    (character is >= 'a' and <= 'z') || (character is >= '0' and <= '9') || character == '-'))
                return false;
        }
        prompt = value.GetString()?.Trim() ?? string.Empty;
        return prompt.Length is > 0 and <= 4096;
    }
    internal static bool TryReadApprovalDecision(JsonElement input, out ApprovalDecisionInput decision)
    {
        decision = default!;
        if (input.ValueKind != JsonValueKind.Object) return false;
        var properties = input.EnumerateObject().ToArray();
        if (properties.Length != 4 || properties.Any(property => property.Name is not ("operationId" or "approvalId" or "decision" or "clientDecisionId")))
            return false;
        if (!input.TryGetProperty("operationId", out var operation) || operation.ValueKind != JsonValueKind.String ||
            !input.TryGetProperty("approvalId", out var approval) || approval.ValueKind != JsonValueKind.String ||
            !input.TryGetProperty("decision", out var action) || action.ValueKind != JsonValueKind.String ||
            !input.TryGetProperty("clientDecisionId", out var client) || client.ValueKind != JsonValueKind.String)
            return false;
        var operationId = operation.GetString();
        var approvalId = approval.GetString();
        var decisionText = action.GetString();
        var clientDecisionId = client.GetString();
        if (!IsOpaqueId(operationId) || !IsOpaqueId(approvalId) || clientDecisionId is null || clientDecisionId.Length is < 16 or > 128 ||
            !clientDecisionId.All(character =>
                (character is >= 'a' and <= 'z') || (character is >= '0' and <= '9') || character == '-') ||
            decisionText is not ("approve" or "reject"))
            return false;
        decision = new(operationId!, approvalId!, decisionText == "approve", clientDecisionId);
        return true;
    }
    internal static bool TryReadFeatureReleaseDecision(JsonElement input, out FeatureReleaseDecisionInput decision)
    {
        decision = default!;
        if (input.ValueKind != JsonValueKind.Object) return false;
        var properties = input.EnumerateObject().ToArray();
        if (properties.Length != 5 || properties.Any(property => property.Name is not ("approvalId" or "releaseDigest" or "expectedRevision" or "decision" or "clientDecisionId")))
            return false;
        if (!input.TryGetProperty("approvalId", out var approval) || approval.ValueKind != JsonValueKind.String ||
            !input.TryGetProperty("releaseDigest", out var release) || release.ValueKind != JsonValueKind.String ||
            !input.TryGetProperty("expectedRevision", out var revision) || !revision.TryGetInt64(out var expectedRevision) ||
            !input.TryGetProperty("decision", out var action) || action.ValueKind != JsonValueKind.String ||
            !input.TryGetProperty("clientDecisionId", out var client) || client.ValueKind != JsonValueKind.String)
            return false;
        var approvalId = approval.GetString();
        var releaseDigest = release.GetString();
        var actionValue = action.GetString();
        var clientDecisionId = client.GetString();
        if (string.IsNullOrWhiteSpace(approvalId) || approvalId.Length != 64 || string.IsNullOrWhiteSpace(releaseDigest) || releaseDigest.Length != 64 ||
            expectedRevision < 1 || actionValue is not ("approve" or "reject") ||
            string.IsNullOrWhiteSpace(clientDecisionId) || clientDecisionId.Length is < 16 or > 128 ||
            !clientDecisionId.All(character =>
                character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
            return false;
        decision = new FeatureReleaseDecisionInput(approvalId, releaseDigest, expectedRevision, string.Equals(actionValue, "approve", StringComparison.Ordinal), clientDecisionId);
        return true;
    }
    private static bool IsOpaqueId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl);
    private static string StableIdempotencyKey(RuntimeRequestContext context, string clientSubmissionId) =>
        "client-submission-" + Hash(RequestScope.Id(context) + "\0" + clientSubmissionId);
    private static string CanonicalJson(JsonElement input)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        WriteCanonical(writer, input);
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
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
            default:
                value.WriteTo(writer);
                break;
        }
    }
    private static async Task<SurfaceFeedState> RetryConflictAsync(ISurfaceFeedNeuron neuron, SurfaceFeedState initial, Func<SurfaceFeedState, Task<SurfaceFeedState>> update, CancellationToken cancellationToken)
    {
        var state = initial;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { return await update(state).WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (RuntimeStateConflictException) when (attempt < 2)
            {
                state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        throw new InvalidOperationException("Surface-feed revision retry exhausted.");
    }
    private static string DeliveryId(string sessionId, long sequence) =>
        Hash(sessionId + "\0" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void DemandActor(RuntimeRequestContext context)
    {
        if (string.IsNullOrWhiteSpace(context.ActorId.Value) || string.IsNullOrWhiteSpace(context.SessionId.Value))
            throw new UnauthorizedAccessException("An actor session is required for the surface feed.");
    }
}
