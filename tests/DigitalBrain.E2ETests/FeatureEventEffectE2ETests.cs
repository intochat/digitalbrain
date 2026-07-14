using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Integrations.Google;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Features;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.Integrations.Salesforce;
using DigitalBrain.Integrations.Salesforce.Grains;
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
        var effects = new RecordingEffectExecutor();
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
        Assert.Null(installation.AppliedOperationKey);

        var outcome = await rail.ApplyAsync(first, approved: true);

        Assert.Equal(InoToolEffectDisposition.Succeeded, outcome.Disposition);
        Assert.Equal(1, effects.Executions);
        Assert.Equal(persistedOperationKey, installation.AppliedOperationKey);
        var outcomeInput = Assert.Single(hub.Inputs);
        Assert.Equal("salesforce.record.update.outcome.v1", outcomeInput.Kind);
        Assert.Equal("event-1", outcomeInput.CausationId);
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

    private sealed class RecordingHub : IFeatureHubGrain
    {
        private readonly Dictionary<string, FeatureInput> _inputs = new(StringComparer.Ordinal);
        public IReadOnlyCollection<FeatureInput> Inputs => _inputs.Values;

        public Task<FeatureFanOutResult> PublishAsync(FeatureInput input)
        {
            _inputs.TryAdd(input.InputId, input);
            return Task.FromResult(new FeatureFanOutResult(input.InputId, 1, 0));
        }

        public Task RegisterAsync(FeatureInstallationRegistration registration) => throw new NotSupportedException();
        public Task<FeatureDraftProposal> CreateDraftAsync(CreateFeatureDraft request) => throw new NotSupportedException();
        public Task<FeatureHubSnapshot> ReadAsync() => throw new NotSupportedException();
        public Task<FeatureApprovalSnapshot> ProposeAsync(FeatureReleaseProposal proposal, long expectedRevision) => throw new NotSupportedException();
        public Task<FeatureApprovalSnapshot> DecideAsync(FeatureApprovalDecision decision, long expectedRevision) => throw new NotSupportedException();
        public Task<FeatureAuthoritySnapshot> GrantAsync(FeatureGrantRequest request, long expectedRevision) => throw new NotSupportedException();
        public Task<FeatureAuthoritySnapshot> InstallAsync(FeatureInstallationRegistration registration, long expectedRevision) => throw new NotSupportedException();
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
        public Task PauseAsync(string reason) => Task.CompletedTask;
        public Task ResumeAsync() => throw new NotSupportedException();
        public Task SwitchReleaseAsync(ReleaseDigest release) => throw new NotSupportedException();
        public Task RollbackAsync() => throw new NotSupportedException();
        public Task<FeatureInstallationSnapshot> ReadAsync() => throw new NotSupportedException();
    }

    private sealed class IntentInstallation(FeatureIntentStatus intent) : ControlledInstallation
    {
        public string? AppliedOperationKey { get; private set; }
        public override Task<FeatureIntentStatus[]> ListPendingIntentsAsync() => Task.FromResult(new[] { intent });
        public override Task ApplyIntentAsync(string operationKey)
        {
            AppliedOperationKey = operationKey;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPlanStore : IInoEffectPlanStore
    {
        private readonly Dictionary<string, PlanBinding> _requests = new(StringComparer.Ordinal);
        public int PlanCount => _requests.Count;

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

    private sealed class RecordingEffectExecutor : IInoEffectExecutor
    {
        public int Executions { get; private set; }

        public bool TryAuthorizeMutation(InoToolRequest request, string actorScope, out InoApprovedTool tool)
        {
            tool = new InoApprovedTool(request.ToolId, request.Scope, request.SafeSummary);
            return request.Scope.StartsWith("signed.", StringComparison.Ordinal);
        }

        public Task<InoToolEffectResult> ExecuteAsync(InoToolEffectRequest request, CancellationToken cancellationToken = default)
        {
            Executions++;
            return Task.FromResult(new InoToolEffectResult(InoToolEffectDisposition.Succeeded, "verified"));
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
}
