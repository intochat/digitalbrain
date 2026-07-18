using DigitalBrain.Integrations.Google;
using DigitalBrain.Integrations.Salesforce;
using DigitalBrain.Integrations.Salesforce.Grains;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Features;
using DigitalBrain.Kernel.Runtime;
using Xunit;

namespace DigitalBrain.E2ETests;

public sealed class FeatureEventEffectE2ETests
{
    private static readonly BrainOwnerId Owner = new("owner-events");
    private static readonly ActorId Actor = new("actor-events");
    private static readonly FeatureInstallationId InstallationId = new("mail-to-salesforce");
    private static readonly ReleaseDigest Release = new(new string('a', 64));
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Gmail_watch_replay_and_duplicate_notification_preserve_one_bounded_event()
    {
        var hub = new RecordingHub();
        var handler = new GmailWatchEventHandler(new FeatureGrains(hub));
        var notification = new GmailMessageReceived(
            Owner,
            "event-1",
            "message-1",
            "thread-1",
            "history-42",
            Now,
            "correlation-1",
            "causation-1",
            "trace-1");

        var first = await handler.HandleAsync(notification);
        var replay = await handler.HandleAsync(notification);

        Assert.Equal(first, replay);
        var input = Assert.Single(hub.Inputs);
        Assert.Equal("gmail.message.received.v1", input.Kind);
        Assert.Equal("causation-1", input.CausationId);
        Assert.DoesNotContain("owner-events", input.PayloadJson, StringComparison.Ordinal);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(input.PayloadJson) < 1024);
    }

    [Fact]
    public async Task Fanout_isolates_slow_installations_and_records_full_inbox_pause_alert()
    {
        var slow = new ControlledInstallation();
        var fast = new ControlledInstallation(FeatureAppendStatus.Accepted);
        var full = new ControlledInstallation(FeatureAppendStatus.Full);
        var grains = new FeatureGrains(new RecordingHub(), new Dictionary<FeatureInstallationId, IFeatureInstallationGrain>
        {
            [new FeatureInstallationId("slow")] = slow,
            [new FeatureInstallationId("fast")] = fast,
            [new FeatureInstallationId("full")] = full
        });
        var input = Input("event-2");
        var batch = new FeatureFanOutState(input,
        [
            new(new FeatureInstallationId("slow"), false),
            new(new FeatureInstallationId("fast"), false),
            new(new FeatureInstallationId("full"), false)
        ]);
        var rail = new FeatureFanOutDeliveryRail(grains);

        var dispatch = rail.DispatchAsync(Owner, batch);
        await fast.Called.WaitAsync(TimeSpan.FromSeconds(2));
        await full.Called.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(dispatch.IsCompleted);
        slow.Complete(FeatureAppendStatus.Accepted);
        var attempts = await dispatch;

        var state = new FeatureHubState(
            [
                new(new FeatureInstallationId("slow"), Release, [input.Kind]),
                new(new FeatureInstallationId("fast"), Release, [input.Kind]),
                new(new FeatureInstallationId("full"), Release, [input.Kind])
            ],
            1,
            [batch],
            [],
            [],
            [],
            []);
        var recorded = FeatureHubTransitions.RecordDeliveryOutcomes(state, input.InputId, attempts, Now);

        Assert.Equal(2, recorded.FanOuts.Single().Deliveries.Count(delivery => delivery.Delivered));
        var alert = Assert.Single(recorded.Alerts);
        Assert.Equal(new FeatureInstallationId("full"), alert.InstallationId);
        Assert.Equal(input.InputId, alert.InputId);
        Assert.Equal("feature inbox full", alert.Reason);
    }

    [Fact]
    public async Task Salesforce_proposal_is_idempotent_waits_for_approval_verifies_and_emits_outcome()
    {
        var payload = SalesforceFeatureEffectPayload.Create(
            new SalesforcePreparedUpdate("{\"version\":1}"u8.ToArray()),
            "update the approved Salesforce field",
            Now.AddHours(24));
        var persistedOperationKey = FeatureIntentKeys.Create(InstallationId, "event-1", "update-account");
        var installation = new IntentInstallation(new FeatureIntentStatus(
            persistedOperationKey,
            FeatureIntentKind.ExternalEffect,
            payload.ToJson(),
            null));
        var hub = new RecordingHub();
        var plans = new RecordingPlanStore();
        var effects = new RecordingEffectExecutor(plans);
        var rail = new SalesforceFeatureEffectRail(
            new FeatureGrains(hub, new Dictionary<FeatureInstallationId, IFeatureInstallationGrain>
            {
                [InstallationId] = installation
            }),
            plans,
            effects,
            new FixedTimeProvider(Now));
        var request = new SalesforceFeatureEffectRequest(
            Owner,
            Actor,
            InstallationId,
            "event-1",
            "update-account",
            "correlation-1",
            "trace-1");

        var first = await rail.ProposeAsync(request);
        var replay = await rail.ProposeAsync(request);

        Assert.Equal(first, replay);
        Assert.Equal(1, plans.PlanCount);
        Assert.Equal(0, effects.Executions);
        Assert.Null(installation.Resolution);

        var outcome = await rail.ApplyAsync(first, approved: true);

        Assert.Equal(InoToolEffectDisposition.Succeeded, outcome.Disposition);
        Assert.Equal(1, effects.Executions);
        Assert.Equal(
            new FeatureEffectResolution(
                persistedOperationKey,
                first.EffectId,
                first.ActorScope,
                InoEffectTerminalKind.Approved,
                Now,
                "verified"),
            installation.Resolution);
        var outcomeInput = Assert.Single(hub.Inputs);
        Assert.Equal("salesforce.record.update.outcome.v1", outcomeInput.Kind);
        Assert.Equal("event-1", outcomeInput.CausationId);
    }

    [Fact]
    public async Task Salesforce_outcome_replay_after_lost_ack_and_time_advance_is_byte_identical()
    {
        var payload = SalesforceFeatureEffectPayload.Create(
            new SalesforcePreparedUpdate("{\"version\":1}"u8.ToArray()),
            "update the approved Salesforce field",
            Now.AddHours(24));
        var persistedOperationKey = FeatureIntentKeys.Create(InstallationId, "event-replay", "update-account");
        var installation = new IntentInstallation(new FeatureIntentStatus(
            persistedOperationKey,
            FeatureIntentKind.ExternalEffect,
            payload.ToJson(),
            null));
        var hub = new RecordingHub(failFirstAcknowledgement: true);
        var plans = new RecordingPlanStore();
        var effects = new RecordingEffectExecutor(plans);
        var timeProvider = new AdjustableTimeProvider(Now);
        var rail = new SalesforceFeatureEffectRail(
            new FeatureGrains(hub, new Dictionary<FeatureInstallationId, IFeatureInstallationGrain>
            {
                [InstallationId] = installation
            }),
            plans,
            effects,
            timeProvider);
        var proposal = await rail.ProposeAsync(new SalesforceFeatureEffectRequest(
            Owner,
            Actor,
            InstallationId,
            "event-replay",
            "update-account",
            "correlation-replay",
            "trace-replay"));

        await Assert.ThrowsAsync<IOException>(() => rail.ApplyAsync(proposal, approved: true));
        timeProvider.Advance(TimeSpan.FromHours(1));

        var replay = await rail.ApplyAsync(proposal, approved: true);

        Assert.Equal(InoToolEffectDisposition.Succeeded, replay.Disposition);
        Assert.Equal(1, effects.Executions);
        Assert.Equal(2, hub.Attempts.Count);
        Assert.Equal(hub.Attempts[0], hub.Attempts[1]);
        Assert.Equal(Now, hub.Attempts[1].OccurredAt);
        Assert.Single(hub.Inputs);
    }

    [Theory]
    [InlineData(InoEffectTerminalKind.Approved, InoToolEffectDisposition.Succeeded, "verified")]
    [InlineData(InoEffectTerminalKind.Declined, InoToolEffectDisposition.Failed, "declined")]
    [InlineData(InoEffectTerminalKind.Failed, InoToolEffectDisposition.Failed, "failed")]
    [InlineData(InoEffectTerminalKind.Expired, InoToolEffectDisposition.Failed, "expired")]
    [InlineData(InoEffectTerminalKind.OutcomeUnknown, InoToolEffectDisposition.OutcomeUnknown, "outcome unknown")]
    public async Task Salesforce_terminal_outcomes_replay_stably_after_lost_ack(
        InoEffectTerminalKind terminalKind,
        InoToolEffectDisposition disposition,
        string safeResult)
    {
        var payload = SalesforceFeatureEffectPayload.Create(
            new SalesforcePreparedUpdate("{\"version\":1}"u8.ToArray()),
            "update the approved Salesforce field",
            Now.AddHours(24));
        var persistedOperationKey = FeatureIntentKeys.Create(InstallationId, "event-terminal", "update-account");
        var installation = new IntentInstallation(new FeatureIntentStatus(
            persistedOperationKey,
            FeatureIntentKind.ExternalEffect,
            payload.ToJson(),
            null));
        var hub = new RecordingHub(failFirstAcknowledgement: true);
        var plans = new RecordingPlanStore();
        var timeProvider = new AdjustableTimeProvider(Now);
        var rail = new SalesforceFeatureEffectRail(
            new FeatureGrains(hub, new Dictionary<FeatureInstallationId, IFeatureInstallationGrain>
            {
                [InstallationId] = installation
            }),
            plans,
            new RecordingEffectExecutor(plans),
            timeProvider);
        var proposal = await rail.ProposeAsync(new SalesforceFeatureEffectRequest(
            Owner,
            Actor,
            InstallationId,
            "event-terminal",
            "update-account",
            "correlation-terminal",
            "trace-terminal"));
        plans.SetTerminal(proposal.EffectId, proposal.ActorScope, terminalKind, disposition, Now, safeResult);

        await Assert.ThrowsAsync<IOException>(() => rail.ApplyAsync(proposal, approved: true));
        timeProvider.Advance(TimeSpan.FromHours(1));

        var replay = await rail.ApplyAsync(proposal, approved: true);

        Assert.Equal(disposition, replay.Disposition);
        Assert.Equal(hub.Attempts[0], hub.Attempts[1]);
        Assert.Contains($"\"terminalKind\":\"{terminalKind}\"", hub.Attempts[1].PayloadJson, StringComparison.Ordinal);
        Assert.Contains($"\"decisionId\":\"{proposal.EffectId}\"", hub.Attempts[1].PayloadJson, StringComparison.Ordinal);
        Assert.Contains($"\"safeResult\":\"{safeResult}\"", hub.Attempts[1].PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Salesforce_outcome_id_cannot_be_reused_for_a_changed_terminal_resolution()
    {
        var payload = SalesforceFeatureEffectPayload.Create(
            new SalesforcePreparedUpdate("{\"version\":1}"u8.ToArray()),
            "update the approved Salesforce field",
            Now.AddHours(24));
        var persistedOperationKey = FeatureIntentKeys.Create(InstallationId, "event-conflict", "update-account");
        var installation = new IntentInstallation(new FeatureIntentStatus(
            persistedOperationKey,
            FeatureIntentKind.ExternalEffect,
            payload.ToJson(),
            null));
        var hub = new RecordingHub(failFirstAcknowledgement: true);
        var plans = new RecordingPlanStore();
        var rail = new SalesforceFeatureEffectRail(
            new FeatureGrains(hub, new Dictionary<FeatureInstallationId, IFeatureInstallationGrain>
            {
                [InstallationId] = installation
            }),
            plans,
            new RecordingEffectExecutor(plans),
            new FixedTimeProvider(Now));
        var proposal = await rail.ProposeAsync(new SalesforceFeatureEffectRequest(
            Owner,
            Actor,
            InstallationId,
            "event-conflict",
            "update-account",
            "correlation-conflict",
            "trace-conflict"));
        plans.SetTerminal(proposal.EffectId, proposal.ActorScope, InoEffectTerminalKind.Approved, InoToolEffectDisposition.Succeeded, Now, "verified");
        await Assert.ThrowsAsync<IOException>(() => rail.ApplyAsync(proposal, approved: true));
        plans.SetTerminal("different-decision", proposal.ActorScope, InoEffectTerminalKind.Approved, InoToolEffectDisposition.Succeeded, Now, "different result");

        await Assert.ThrowsAsync<FeatureConcurrencyException>(() => rail.ApplyAsync(proposal, approved: true));
    }

    [Fact]
    public async Task Decline_wins_a_later_approval_without_provider_execution()
    {
        var payload = SalesforceFeatureEffectPayload.Create(
            new SalesforcePreparedUpdate("{\"version\":1}"u8.ToArray()),
            "update the approved Salesforce field",
            Now.AddHours(24));
        var persistedOperationKey = FeatureIntentKeys.Create(InstallationId, "event-declined", "update-account");
        var installation = new IntentInstallation(new FeatureIntentStatus(
            persistedOperationKey,
            FeatureIntentKind.ExternalEffect,
            payload.ToJson(),
            null));
        var hub = new RecordingHub();
        var plans = new RecordingPlanStore();
        var effects = new RecordingEffectExecutor(plans);
        var rail = new SalesforceFeatureEffectRail(
            new FeatureGrains(hub, new Dictionary<FeatureInstallationId, IFeatureInstallationGrain>
            {
                [InstallationId] = installation
            }),
            plans,
            effects,
            new FixedTimeProvider(Now));
        var proposal = await rail.ProposeAsync(new SalesforceFeatureEffectRequest(
            Owner,
            Actor,
            InstallationId,
            "event-declined",
            "update-account",
            "correlation-declined",
            "trace-declined"));

        var outcome = await rail.ApplyAsync(proposal, approved: false);
        var replay = await rail.ApplyAsync(proposal, approved: true);

        Assert.Equal(InoToolEffectDisposition.Failed, outcome.Disposition);
        Assert.Equal(outcome, replay);
        Assert.Equal(1, plans.Declines);
        Assert.Equal(0, effects.Executions);
        Assert.Equal(
            new FeatureEffectResolution(
                persistedOperationKey,
                proposal.DecisionId,
                proposal.ActorScope,
                InoEffectTerminalKind.Declined,
                Now,
                "The Salesforce update was not approved. No external action was performed."),
            installation.Resolution);
        var outcomeInput = Assert.Single(hub.Inputs);
        Assert.Equal(SalesforceFeatureEffectRail.OutcomeKind, outcomeInput.Kind);
        Assert.Equal("event-declined", outcomeInput.CausationId);
        Assert.Contains("Failed", outcomeInput.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Salesforce_effect_handler_requires_connector_verification()
    {
        var gateway = new RecordingSalesforceMutationGateway();
        var handler = new SalesforceUpdateEffectHandler(gateway);

        var result = await handler.ApplyAsync("actor-scope", "{\"version\":1}"u8.ToArray());

        Assert.Equal(InoToolEffectDisposition.Succeeded, result.Disposition);
        Assert.Equal(1, gateway.Applies);
        Assert.Equal(1, gateway.Verifications);
    }

    private static FeatureInput Input(string inputId) => new(
        inputId,
        "gmail.message.received.v1",
        "{\"messageId\":\"message-1\"}",
        Now,
        "correlation-1",
        "trace-1",
        "causation-1");

    private sealed class FeatureGrains(
        IFeatureHubGrain hub,
        IReadOnlyDictionary<FeatureInstallationId, IFeatureInstallationGrain>? installations = null)
        : IFeatureGrainResolver
    {
        public IFeatureHubGrain Hub(BrainOwnerId ownerId) => hub;

        public IFeatureInstallationGrain Installation(
            BrainOwnerId ownerId,
            FeatureInstallationId installationId) =>
            installations is not null && installations.TryGetValue(installationId, out var installation)
                ? installation
                : throw new KeyNotFoundException();
    }

    private sealed class RecordingHub(bool failFirstAcknowledgement = false) : IFeatureHubGrain
    {
        private readonly Dictionary<string, FeatureInput> _inputs = new(StringComparer.Ordinal);
        private int _publications;
        public List<FeatureInput> Attempts { get; } = [];
        public IReadOnlyCollection<FeatureInput> Inputs => _inputs.Values;

        public Task<FeatureFanOutResult> PublishAsync(FeatureInput input)
        {
            Attempts.Add(input);
            if (_inputs.TryGetValue(input.InputId, out var existing) && existing != input)
                throw new FeatureConcurrencyException("The fan-out input id is already bound to different content.");
            _inputs.TryAdd(input.InputId, input);
            if (failFirstAcknowledgement && ++_publications == 1)
                throw new IOException("The publication acknowledgement was lost.");
            return Task.FromResult(new FeatureFanOutResult(input.InputId, 1, 0));
        }

        public Task RegisterAsync(FeatureInstallationRegistration registration) => throw new NotSupportedException();
        public Task<FeatureDraft> CreateDraftAsync(CreateFeatureDraft request) => throw new NotSupportedException();
        public Task<FeatureDraft?> ReadDraftAsync(FeatureDraftId draftId) => throw new NotSupportedException();
        public Task<FeatureDraft?> ReadInstalledDraftAsync(FeatureInstallationId installationId, ReleaseDigest release) => throw new NotSupportedException();
        public Task<FeatureDraft> ReviseBehaviorAsync(ReviseFeatureBehavior command) => throw new NotSupportedException();
        public Task<FeatureDraft> ReviseSourceAsync(ReviseFeatureSource command) => throw new NotSupportedException();
        public Task<FeatureDraft> AcceptSuggestedChangeAsync(AcceptSuggestedChange command) => throw new NotSupportedException();
        public Task<FeatureDraft> RejectSuggestedChangeAsync(RejectSuggestedChange command) => throw new NotSupportedException();
        public Task<FeatureDraft> RecordVerificationAsync(RecordFeatureVerification command) => throw new NotSupportedException();
        public Task<FeatureDraftInstallationReservation> AcquireDraftInstallationReservationAsync(InstallFeatureVersion command, ActorId actorId) => throw new NotSupportedException();
        public Task<FeatureDraftInstallationReservation?> ReadDraftInstallationReservationAsync(FeatureDraftId draftId) => throw new NotSupportedException();
        public Task<FeatureDraftInstallationResetObligation?> ReadDraftInstallationResetAsync(FeatureDraftId draftId) => throw new NotSupportedException();
        public Task<FeatureDraftInstallationResetPreparation> ResetDraftInstallationReservationAsync(ResetFeatureDraftInstallationReservation command, ActorId actorId) => throw new NotSupportedException();
        public Task<FeatureDraft> CompleteDraftInstallationReservationResetAsync(FeatureDraftId draftId, string idempotencyId, ActorId actorId) => throw new NotSupportedException();
        public Task<FeatureDraft> MarkDraftInstalledAsync(MarkFeatureDraftInstalled command) => throw new NotSupportedException();
        public Task<FeatureHubSnapshot> ReadAsync() => throw new NotSupportedException();
        public Task<FeatureApprovalSnapshot> ProposeAsync(FeatureReleaseProposal proposal, long expectedRevision) => throw new NotSupportedException();
        public Task<FeatureApprovalSnapshot> DecideAsync(FeatureApprovalDecision decision, long expectedRevision) => throw new NotSupportedException();
        public Task<FeatureAuthoritySnapshot> GrantAsync(FeatureGrantRequest request, long expectedRevision) => throw new NotSupportedException();
        public Task<FeatureAuthoritySnapshot> InstallAsync(FeatureInstallationRegistration registration, long expectedRevision) => throw new NotSupportedException();
        public Task<FeaturePublicationTicket> PrepareActivePublicationAsync(FeatureInstallationId installationId) => throw new NotSupportedException();
        public Task<FeaturePublicationReceipt> ConfirmActivePublicationAsync(FeaturePublicationReceipt receipt) => throw new NotSupportedException();
        public Task RevokeAsync(FeatureGrantRevocation revocation, long expectedRevision) => throw new NotSupportedException();
        public Task PauseInstallationAsync(FeatureInstallationId installationId, string reason, long expectedRevision) => throw new NotSupportedException();
        public Task ResumeInstallationAsync(FeatureInstallationId installationId, long expectedRevision) => throw new NotSupportedException();
        public Task<FeatureAuthoritySnapshot> RollbackInstallationAsync(FeatureInstallationId installationId, long expectedRevision) => throw new NotSupportedException();
        public Task<FeatureGrantSnapshot?> ReadGrantAsync(FeatureGrantLookup lookup) => throw new NotSupportedException();
    }

    private class ControlledInstallation : IFeatureInstallationGrain
    {
        private readonly TaskCompletionSource<FeatureAppendStatus> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ControlledInstallation(FeatureAppendStatus? immediate = null)
        {
            if (immediate is { } status) _completion.SetResult(status);
        }

        public Task Called => _called.Task;
        public void Complete(FeatureAppendStatus status) => _completion.TrySetResult(status);

        public Task<FeatureAppendStatus> AppendAsync(FeatureInput input)
        {
            _called.TrySetResult();
            return _completion.Task;
        }

        public Task InitializeAsync(ReleaseDigest release) => throw new NotSupportedException();
        public Task<FeatureRunClaim?> ClaimAsync(string hostId, TimeSpan leaseDuration) => throw new NotSupportedException();
        public Task<FeatureFailureDisposition> FailAsync(FeatureLeaseFence fence, DateTimeOffset retryAt, string safeFailure) => throw new NotSupportedException();
        public Task<FeatureAppendStatus> RecordScheduleOccurrenceAsync(FeatureScheduleOccurrence occurrence) => throw new NotSupportedException();
        public Task<FeatureCompletionReceipt> CommitAsync(FeatureRunCommit commit) => throw new NotSupportedException();
        public virtual Task<FeatureIntentStatus[]> ListPendingIntentsAsync() => throw new NotSupportedException();
        public virtual Task ApplyIntentAsync(string operationKey) => throw new NotSupportedException();
        public virtual Task DeclineIntentAsync(string operationKey) => throw new NotSupportedException();
        public virtual Task ResolveIntentAsync(FeatureEffectResolution resolution) => throw new NotSupportedException();
        public Task PauseAsync(string reason) => Task.CompletedTask;
        public Task ResumeAsync() => throw new NotSupportedException();
        public Task SwitchReleaseAsync(ReleaseDigest release) => throw new NotSupportedException();
        public Task<FeatureRuntimeReservationSnapshot> EstablishReservationAsync(FeatureRuntimeReservation reservation) => throw new NotSupportedException();
        public Task<FeatureRuntimeReservationSnapshot?> ReadReservationAsync() => throw new NotSupportedException();
        public Task ActivateReservedReleaseAsync(FeatureRuntimeReservation reservation) => throw new NotSupportedException();
        public Task ResetReservedReleaseAsync(FeatureRuntimeReservation reservation, bool requireRuntimeAbsence) => throw new NotSupportedException();
        public Task ReleaseReservationAsync(FeatureRuntimeReservationRelease release) => throw new NotSupportedException();
        public Task BeginReleaseSwitchAsync(ReleaseDigest release, string operationToken) => throw new NotSupportedException();
        public Task ConfirmReleaseSwitchAsync(ReleaseDigest release) => throw new NotSupportedException();
        public Task ClearBackpressurePauseAsync() => throw new NotSupportedException();
        public Task DiscardUnpublishedAsync(ReleaseDigest release, bool requireAbsent) => throw new NotSupportedException();
        public Task RestoreUnpublishedCandidateAsync(
            ReleaseDigest candidateRelease,
            ReleaseDigest expectedActiveRelease,
            ReleaseDigest? expectedPreviousRelease,
            long minimumFromRevision) => throw new NotSupportedException();
        public Task RollbackAsync() => throw new NotSupportedException();
        public Task<FeatureInstallationSnapshot> ReadAsync() => throw new NotSupportedException();
    }

    private sealed class IntentInstallation(FeatureIntentStatus intent) : ControlledInstallation
    {
        public FeatureEffectResolution? Resolution { get; private set; }
        public override Task<FeatureIntentStatus[]> ListPendingIntentsAsync() => Task.FromResult(new[] { intent });
        public override Task ResolveIntentAsync(FeatureEffectResolution resolution)
        {
            Resolution = resolution;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPlanStore : IInoEffectPlanStore
    {
        private readonly Dictionary<string, PlanBinding> _requests = new(StringComparer.Ordinal);
        public int PlanCount => _requests.Count;
        public int Declines { get; private set; }
        public InoToolEffectResult? TerminalResult { get; private set; }

        public Task<InoToolRequest> PrepareAsync(string actorScope, string operationId, string toolId, byte[] payloadUtf8, string safeSummary, DateTimeOffset expiresAt, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InoToolRequest> PrepareIdempotentAsync(string idempotencyKey, string actorScope, string operationId, string toolId, byte[] payloadUtf8, string safeSummary, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
        {
            var binding = new PlanBinding(
                actorScope,
                operationId,
                toolId,
                Convert.ToHexString(payloadUtf8),
                safeSummary,
                expiresAt);
            if (!_requests.TryGetValue(idempotencyKey, out var existing))
            {
                var request = new InoToolRequest(toolId, InoToolAccess.Mutation, "signed." + idempotencyKey, safeSummary);
                _requests.Add(idempotencyKey, binding with { Request = request });
                return Task.FromResult(request);
            }
            if (existing with { Request = null } != binding)
                throw new InvalidOperationException("Idempotent plan binding changed.");
            return Task.FromResult(existing.Request!);
        }

        public Task<InoToolEffectResult> DeclineAsync(
            InoToolRequest request,
            string actorScope,
            string decisionId,
            CancellationToken cancellationToken = default)
        {
            if (TerminalResult is null)
            {
                Declines++;
                TerminalResult = new InoToolEffectResult(
                    InoToolEffectDisposition.Failed,
                    "The Salesforce update was not approved. No external action was performed.");
                TerminalDecision = new InoEffectDecision(
                    decisionId,
                    actorScope,
                    InoEffectTerminalKind.Declined,
                    Now);
            }
            return Task.FromResult(TerminalResult);
        }

        public InoEffectDecision? TerminalDecision { get; private set; }

        public Task<InoEffectDecision?> ReadDecisionAsync(
            InoToolRequest request,
            string actorScope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(TerminalDecision);

        public void RecordExecution(InoToolEffectRequest request, InoToolEffectResult result)
        {
            TerminalResult = result;
            TerminalDecision = new InoEffectDecision(
                request.EffectId,
                request.ActorScope,
                InoEffectTerminalKind.Approved,
                Now);
        }

        public void SetTerminal(
            string decisionId,
            string actorScope,
            InoEffectTerminalKind terminalKind,
            InoToolEffectDisposition disposition,
            DateTimeOffset resolvedAt,
            string safeResult)
        {
            TerminalResult = new InoToolEffectResult(disposition, safeResult);
            TerminalDecision = new InoEffectDecision(decisionId, actorScope, terminalKind, resolvedAt);
        }

        private sealed record PlanBinding(
            string ActorScope,
            string OperationId,
            string ToolId,
            string Payload,
            string SafeSummary,
            DateTimeOffset ExpiresAt)
        {
            public InoToolRequest? Request { get; init; }
        }
    }

    private sealed class RecordingEffectExecutor(RecordingPlanStore? plans = null) : IInoEffectExecutor
    {
        public int Executions { get; private set; }

        public bool TryAuthorizeMutation(InoToolRequest request, string actorScope, out InoApprovedTool tool)
        {
            tool = new InoApprovedTool(request.ToolId, request.Scope, request.SafeSummary);
            return request.Scope.StartsWith("signed.", StringComparison.Ordinal);
        }

        public Task<InoToolEffectResult> ExecuteAsync(InoToolEffectRequest request, CancellationToken cancellationToken = default)
        {
            if (plans?.TerminalResult is { } terminal)
                return Task.FromResult(terminal);
            Executions++;
            var result = new InoToolEffectResult(InoToolEffectDisposition.Succeeded, "verified");
            plans?.RecordExecution(request, result);
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingSalesforceMutationGateway : ISalesforceMutationGateway
    {
        public int Applies { get; private set; }
        public int Verifications { get; private set; }

        public Task<SalesforceMutationPreviewResult> PreviewAsync(string actorScope, SalesforceUpdatePreviewRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SalesforceMutationApplyResult> ApplyAsync(string actorScope, SalesforcePreparedUpdate preparedUpdate, CancellationToken cancellationToken = default)
        {
            Applies++;
            return Task.FromResult(new SalesforceMutationApplyResult(SalesforceMutationStatus.Applied));
        }

        public Task<SalesforceMutationVerificationResult> VerifyAsync(string actorScope, SalesforcePreparedUpdate preparedUpdate, CancellationToken cancellationToken = default)
        {
            Verifications++;
            return Task.FromResult(new SalesforceMutationVerificationResult(true));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }
}
