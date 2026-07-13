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

public sealed class InoWorkerConflictRecoveryTests : NeuronTestBase
{
    private readonly PostResultBarrierTimeProvider _timeProvider = new();
    private readonly WorkflowCallCounter _workflowCalls = new();

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
                services.AddSingleton(_workflowCalls);
                services.AddSingleton<IAgentWorkflowRunner, ConflictWorkflowRunner>();
                services.AddSingleton<IInoToolGateway, ClosedInoToolGateway>();
            });
    }

    protected override void ConfigureClient(IClientBuilder builder) =>
        builder.Configure<ClientMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromSeconds(10));

    [Fact]
    public async Task Worker_reconciles_a_post_result_revision_conflict_without_running_the_workflow_again()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == "DigitalBrain.Ino.Worker",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.PropagationData
        };
        ActivitySource.AddActivityListener(listener);

        var tenant = new TenantId("tenant");
        var workspace = new WorkspaceId("workspace");
        var principal = new PrincipalRef("principal", PrincipalKind.User);
        var identity = new ConversationIdentity(tenant, workspace, principal, "conversation-worker-conflict");
        var conversation = Grain<IConversationNeuron>(RuntimeStateKeys.Conversation(
            tenant,
            workspace,
            principal,
            identity.ConversationId));
        var now = DateTimeOffset.UtcNow;
        const string operationId = "operation-worker-conflict";
        var accepted = OperationOutboxRecord.Create(
            "accepted-worker-conflict",
            operationId,
            InoOperationPhase.Accepted,
            1,
            now,
            identity.ConversationId,
            2,
            "request-worker-conflict",
            RuntimeStateKeys.Conversation(tenant, workspace, principal, identity.ConversationId),
            new OperationFeedView(
                "command-worker-conflict",
                string.Empty,
                false,
                null,
                null,
                null,
                [new OperationFeedTurn(
                    "command-worker-conflict",
                    "user",
                    "return a safe status",
                    InoConversationStates.Queued)]));

        try
        {
            var initialized = await conversation.InitializeAsync(0, identity);
            await conversation.BeginOperationAsync(
                initialized.Revision,
                "command-worker-conflict",
                new string('a', 64),
                operationId,
                "return a safe status",
                "request-worker-conflict",
                new ConversationOutboxEntry("accepted-worker-conflict", "surface-feed", accepted.ToPayloadUtf8(), now, null),
                now);

            await _timeProvider.PostResultReached.WaitAsync(TimeSpan.FromSeconds(10));
            await RecordMigrationWithRetryAsync(conversation, "test-post-result-revision-conflict");
            _timeProvider.ReleasePostResult();

            var terminal = await WaitForTerminalAsync(conversation, operationId, TimeSpan.FromSeconds(12));
            Assert.Equal(ConversationOperationStatus.Succeeded, terminal.Status);
            Assert.Equal(1, _workflowCalls.Count);
        }
        finally
        {
            _timeProvider.ReleasePostResult();
        }
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

    private static async Task RecordMigrationWithRetryAsync(IConversationNeuron conversation, string migrationId)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var state = await conversation.ReadAsync();
            try
            {
                await conversation.RecordMigrationAsync(state.Revision, migrationId);
                return;
            }
            catch (RuntimeStateConflictException) when (attempt < 2)
            {
                // The durable dispatcher may commit an earlier phase between the read and this test-only write.
            }
        }
    }

    private sealed class ConflictWorkflowRunner(
        PostResultBarrierTimeProvider timeProvider,
        WorkflowCallCounter workflowCalls) : IAgentWorkflowRunner
    {
        public Task<InoWorkflowResult> ExecuteAsync(
            InoWorkflowRequest request,
            CancellationToken cancellationToken = default)
        {
            workflowCalls.Increment();
            timeProvider.ArmForWorkerPostResult();
            return Task.FromResult(new InoWorkflowResult(
                "The status is ready.",
                new WorkflowReference("test", "workflow-worker-conflict", "session-worker-conflict")));
        }
    }

    private sealed class WorkflowCallCounter
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);
        public void Increment() => Interlocked.Increment(ref _count);
    }

    private sealed class PostResultBarrierTimeProvider : TimeProvider
    {
        private readonly TaskCompletionSource _postResultReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new(false);
        private int _armed;
        private int _blocked;

        public Task PostResultReached => _postResultReached.Task;

        public void ArmForWorkerPostResult() => Volatile.Write(ref _armed, 1);
        public void ReleasePostResult() => _release.Set();

        public override DateTimeOffset GetUtcNow()
        {
            if (Volatile.Read(ref _armed) == 1 &&
                string.Equals(Activity.Current?.OperationName, "ino.operation.execute", StringComparison.Ordinal) &&
                Interlocked.CompareExchange(ref _blocked, 1, 0) == 0)
            {
                _postResultReached.TrySetResult();
                _release.Wait(TimeSpan.FromSeconds(10));
            }

            return DateTimeOffset.UtcNow;
        }
    }
}
