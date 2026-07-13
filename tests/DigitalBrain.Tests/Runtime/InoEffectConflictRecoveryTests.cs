using System.Diagnostics;
using System.Threading;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Hosting;

namespace DigitalBrain.Tests.Runtime;

public sealed class InoEffectConflictRecoveryTests : NeuronTestBase
{
    private readonly PostEffectResultBarrierTimeProvider _timeProvider = new();
    private readonly EffectCallCounter _effectCalls = new();

    protected override void ConfigureSilo(ISiloBuilder builder)
    {
        var keyRing = new RuntimeStateKeyRing(
            1,
            new Dictionary<int, byte[]> { [1] = Enumerable.Repeat((byte)1, 32).ToArray() },
            Enumerable.Repeat((byte)2, 32).ToArray());

        builder
            .UseInMemoryReminderService()
            .AddMemoryGrainStorage(RuntimeStateStorageProviders.Conversations)
            .AddMemoryGrainStorage(RuntimeStateStorageProviders.SurfaceFeeds)
            .Configure<ReminderOptions>(options => options.MinimumReminderPeriod = TimeSpan.FromSeconds(1))
            .Configure<SiloMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromSeconds(10))
            .ConfigureServices(services =>
            {
                services.AddSingleton<IRuntimeStateKeyRing>(keyRing);
                services.AddSingleton(new EncryptedRuntimeStateProtector(keyRing));
                services.AddSingleton<TimeProvider>(_timeProvider);
                services.AddSingleton(_timeProvider);
                services.AddSingleton(_effectCalls);
                services.AddSingleton<IAgentWorkflowRunner, UnusedWorkflowRunner>();
                services.AddSingleton<IInoToolGateway, SucceedingEffectGateway>();
            });
    }

    protected override void ConfigureClient(IClientBuilder builder) =>
        builder.Configure<ClientMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromSeconds(10));

    [Fact]
    public async Task Worker_reconciles_a_post_effect_result_revision_conflict_without_reexecuting_the_effect()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == "DigitalBrain.Ino.Worker",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.PropagationData
        };
        ActivitySource.AddActivityListener(listener);

        var owner = new BrainOwnerId("owner");
        var actor = new ActorId("principal");
        var identity = new ConversationIdentity(owner, actor, "conversation-effect-conflict");
        var conversationKey = RuntimeStateKeys.Conversation(owner, actor, identity.ConversationId);
        var conversation = Grain<IConversationNeuron>(conversationKey);
        var now = DateTimeOffset.UtcNow;
        const string operationId = "operation-effect-conflict";
        const string commandId = "command-effect-conflict";
        const string approvalId = "approval-effect-conflict";
        const string effectId = "effect-effect-conflict";
        const string toolId = "test.effect";
        var workflow = new WorkflowReference("test", "workflow-effect-conflict", "session-effect-conflict");

        try
        {
            var initialized = await conversation.InitializeAsync(0, identity);
            await conversation.BeginOperationAsync(
                initialized.Revision,
                commandId,
                new string('a', 64),
                operationId,
                "apply the safe test change",
                "request-effect-conflict",
                PhaseOutbox(identity, conversationKey, commandId, operationId, "request-effect-conflict", InoOperationPhase.Accepted, 1, now),
                now);

            var queued = await conversation.ReadAsync();
            var claim = await conversation.TryClaimOperationAsync(
                queued.Revision,
                operationId,
                "workflow-setup",
                now.AddMilliseconds(1),
                TimeSpan.FromMinutes(1));
            var claimed = claim.Operation!;
            var approval = new ApprovalRecord(approvalId, operationId, effectId, "requested", 1, now.AddSeconds(1));
            var effect = new EffectRecord(effectId, operationId, toolId, "workspace", "awaiting-approval", effectId, 1);
            var approvalRequested = await conversation.RequestApprovalWithAssistantAsync(
                claim.State.Revision,
                operationId,
                approval,
                effect,
                "Approval is required for the safe test change.",
                PhaseOutbox(
                    identity,
                    conversationKey,
                    commandId,
                    operationId,
                    "request-effect-conflict",
                    InoOperationPhase.AwaitingApproval,
                    checked(claimed.Version + 1),
                    now.AddSeconds(1),
                    toolId,
                    effectId,
                    approvalId,
                    workflow),
                now.AddSeconds(1),
                workflow,
                new ConversationLeaseFence("workflow-setup", claimed.Attempt));
            var actorScope = RequestScope.Id(identity.OwnerId, identity.ActorId);
            var approved = await conversation.DecideApprovalWithAssistantAsync(
                approvalRequested.Revision,
                operationId,
                approvalId,
                approved: true,
                "decision-effect-conflict",
                actorScope,
                "Approval recorded.",
                PhaseOutbox(
                    identity,
                    conversationKey,
                    commandId,
                    operationId,
                    "request-effect-conflict",
                    InoOperationPhase.Approved,
                    checked(approvalRequested.Operations.Single(operation => operation.OperationId == operationId).Version + 1),
                    now.AddSeconds(2),
                    toolId,
                    effectId,
                    approvalId,
                    workflow),
                now.AddSeconds(2));

            await Grain<IInoOperationWorkerGrain>(conversationKey + "|" + operationId).ScheduleAsync();
            await _timeProvider.PostEffectResultReached.WaitAsync(TimeSpan.FromSeconds(10));
            var beforeConflict = await conversation.ReadAsync();
            await conversation.RecordMigrationAsync(beforeConflict.Revision, "test-post-effect-result-revision-conflict");
            _timeProvider.ReleasePostEffectResult();

            var terminal = await WaitForTerminalAsync(conversation, operationId, TimeSpan.FromSeconds(12));
            Assert.Equal(ConversationOperationStatus.Succeeded, terminal.Status);
            Assert.Equal("succeeded", terminal.Effect!.State);
            Assert.Equal(1, _effectCalls.Count);
            var records = (await conversation.ReadAsync()).Outbox
                .Select(entry =>
                {
                    Assert.True(OperationOutboxRecord.TryRead(entry.PayloadUtf8, out var record));
                    return record!;
                })
                .Where(record => string.Equals(record.OperationId, operationId, StringComparison.Ordinal))
                .ToArray();
            var applying = records.Single(record => record.Phase == InoOperationPhase.ApplyingEffect);
            var succeeded = records.Single(record => record.Phase == InoOperationPhase.Succeeded);
            Assert.Equal(toolId, applying.ToolId);
            Assert.Equal(effectId, applying.EffectId);
            Assert.Equal(toolId, succeeded.ToolId);
            Assert.Equal(effectId, succeeded.EffectId);
        }
        finally
        {
            _timeProvider.ReleasePostEffectResult();
        }
    }

    private static ConversationOutboxEntry PhaseOutbox(
        ConversationIdentity identity,
        string conversationKey,
        string commandId,
        string operationId,
        string requestId,
        InoOperationPhase phase,
        long operationVersion,
        DateTimeOffset occurredAt,
        string? toolId = null,
        string? effectId = null,
        string? approvalId = null,
        WorkflowReference? workflow = null)
    {
        var record = OperationOutboxRecord.Create(
            $"operation:{operationId}:phase:{phase.ToString().ToLowerInvariant()}:v:{operationVersion}",
            operationId,
            phase,
            operationVersion,
            occurredAt,
            identity.ConversationId,
            operationVersion,
            requestId,
            conversationKey,
            new OperationFeedView(commandId, string.Empty, false, null, approvalId, null, []),
            toolId,
            effectId,
            approvalId,
            workflow);
        return new ConversationOutboxEntry(record.EventId, "surface-feed", record.ToPayloadUtf8(), occurredAt, null);
    }

    private static async Task<ConversationOperation> WaitForTerminalAsync(
        IConversationNeuron conversation,
        string operationId,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var operation = (await conversation.ReadAsync()).Operations.Single(candidate =>
                string.Equals(candidate.OperationId, operationId, StringComparison.Ordinal));
            if (operation.Status is ConversationOperationStatus.Succeeded or ConversationOperationStatus.Failed or
                ConversationOperationStatus.OutcomeUnknown or ConversationOperationStatus.Cancelled)
                return operation;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        var final = (await conversation.ReadAsync()).Operations.Single(candidate =>
            string.Equals(candidate.OperationId, operationId, StringComparison.Ordinal));
        throw new Xunit.Sdk.XunitException(
            $"Operation {operationId} did not reach a terminal state; status={final.Status}; attempt={final.Attempt}; " +
            $"version={final.Version}; leaseOwnerPresent={!string.IsNullOrWhiteSpace(final.LeaseOwner)}; " +
            $"leaseExpiresAt={final.LeaseExpiresAt:O}; nextAttemptAt={final.NextAttemptAt:O}; " +
            $"terminalPolicy={final.TerminalPolicy}.");
    }

    private sealed class UnusedWorkflowRunner : IAgentWorkflowRunner
    {
        public Task<InoWorkflowResult> ExecuteAsync(
            InoWorkflowRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("An approved effect must not invoke the workflow runner again.");
    }

    private sealed class SucceedingEffectGateway(
        PostEffectResultBarrierTimeProvider timeProvider,
        EffectCallCounter effectCalls) : IInoToolGateway
    {
        public bool TryAuthorizeMutation(InoToolRequest request, string actorScope, out InoApprovedTool tool)
        {
            tool = new InoApprovedTool("test.effect", "workspace", "safe test change");
            return true;
        }

        public Task<InoToolEffectResult> ExecuteApprovedAsync(
            InoToolEffectRequest request,
            CancellationToken cancellationToken = default)
        {
            effectCalls.Increment();
            timeProvider.ArmForWorkerPostEffectResult();
            return Task.FromResult(new InoToolEffectResult(InoToolEffectDisposition.Succeeded, "The safe test change completed."));
        }
    }

    private sealed class EffectCallCounter
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);
        public void Increment() => Interlocked.Increment(ref _count);
    }

    private sealed class PostEffectResultBarrierTimeProvider : TimeProvider
    {
        private readonly TaskCompletionSource _postEffectResultReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new(false);
        private int _armed;
        private int _blocked;

        public Task PostEffectResultReached => _postEffectResultReached.Task;

        public void ArmForWorkerPostEffectResult() => Volatile.Write(ref _armed, 1);
        public void ReleasePostEffectResult() => _release.Set();

        public override DateTimeOffset GetUtcNow()
        {
            if (Volatile.Read(ref _armed) == 1 &&
                string.Equals(Activity.Current?.OperationName, "ino.operation.execute", StringComparison.Ordinal) &&
                Interlocked.CompareExchange(ref _blocked, 1, 0) == 0)
            {
                _postEffectResultReached.TrySetResult();
                _release.Wait(TimeSpan.FromSeconds(10));
            }

            return DateTimeOffset.UtcNow;
        }
    }
}
