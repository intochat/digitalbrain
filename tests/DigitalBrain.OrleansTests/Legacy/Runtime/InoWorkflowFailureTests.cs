using System.Diagnostics;
using System.Reflection;
using System.Threading;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.OrleansTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Configuration;
using Orleans.Hosting;

namespace DigitalBrain.Tests.Runtime;

public sealed class InoWorkflowFailureTests : NeuronTestBase
{
    private readonly FailingWorkflowRunner _workflowRunner = new();

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
                services.AddSingleton(_workflowRunner);
                services.AddSingleton<IAgentWorkflowRunner>(_workflowRunner);
                services.AddSingleton<IInoEffectExecutor, DisabledInoEffectExecutor>();
            });
    }

    protected override void ConfigureClient(IClientBuilder builder) =>
        builder.Configure<ClientMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromSeconds(10));

    [Fact]
    public async Task Workflow_failure_is_terminal_without_a_durable_retry()
    {
        _workflowRunner.SetFailure(new InvalidOperationException("provider failure"));

        var (operation, state) = await SubmitAndWaitForTransitionAsync("workflow-failure");

        Assert.Equal(ConversationOperationStatus.Failed, operation.Status);
        Assert.Equal(ConversationTerminalPolicy.NeverRetry, operation.TerminalPolicy);
        Assert.Equal(1, _workflowRunner.CallCount);
        var terminal = state.Outbox
            .Select(entry => OperationOutboxRecord.TryRead(entry.PayloadUtf8, out var record) ? record : null)
            .Single(record => record?.Phase == InoOperationPhase.Failed)!;
        var userTurn = Assert.Single(terminal.View!.Turns, turn =>
            string.Equals(turn.CommandId, operation.CommandId, StringComparison.Ordinal));
        Assert.Equal(InoConversationStates.Failed, userTurn.State);
    }

    [Fact]
    public async Task Workflow_deadline_is_terminal_without_a_durable_retry()
    {
        _workflowRunner.SetFailure(new OperationCanceledException("provider deadline"));

        var (operation, _) = await SubmitAndWaitForTransitionAsync("workflow-deadline");

        Assert.Equal(ConversationOperationStatus.Failed, operation.Status);
        Assert.Equal(ConversationTerminalPolicy.NeverRetry, operation.TerminalPolicy);
        Assert.Equal(1, _workflowRunner.CallCount);
    }

    [Fact]
    public async Task Feature_invocation_conflict_is_outcome_unknown_and_requires_verification()
    {
        _workflowRunner.SetFailure(new FeatureCapabilityOutcomeUnknownException());

        var (operation, state) = await SubmitAndWaitForTransitionAsync("feature-outcome-unknown");

        Assert.Equal(ConversationOperationStatus.OutcomeUnknown, operation.Status);
        Assert.Equal(ConversationTerminalPolicy.VerifyBeforeRetry, operation.TerminalPolicy);
        Assert.Equal(1, _workflowRunner.CallCount);
        Assert.Contains(state.Outbox, entry =>
            OperationOutboxRecord.TryRead(entry.PayloadUtf8, out var record) &&
            record is { Phase: InoOperationPhase.OutcomeUnknown });
    }

    [Fact]
    public async Task Feature_outcome_unknown_reconciles_a_concurrent_revision_before_returning()
    {
        var now = DateTimeOffset.UtcNow;
        var fence = new ConversationLeaseFence("lease-owner", 1);
        var operation = new ConversationOperation(
            "operation-conflict",
            "command-conflict",
            ConversationOperationStatus.Running,
            fence.Attempt,
            null,
            fence.LeaseOwner,
            now.AddMinutes(1),
            ConversationTerminalPolicy.NeverRetry,
            null,
            null,
            now,
            2,
            RequestId: "request-conflict");
        var stale = RunningState(4, operation, now);
        var current = RunningState(5, operation, now);
        var conversation = DispatchProxy.Create<IConversationNeuron, ConflictingConversationProxy>();
        var proxy = (ConflictingConversationProxy)(object)conversation;
        proxy.Current = current;
        var worker = new InoOperationWorkerGrain(
            null!,
            _workflowRunner,
            new DisabledInoEffectExecutor(),
            [],
            TimeProvider.System,
            NullLogger<InoOperationWorkerGrain>.Instance);
        var method = typeof(InoOperationWorkerGrain).GetMethod(
            "RecordUnknownAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Xunit.Sdk.XunitException("RecordUnknownAsync was not found.");

        await (Task)(method.Invoke(
            worker,
            [conversation, stale, operation, "Verify the Feature result before retrying.", fence])
            ?? throw new Xunit.Sdk.XunitException("RecordUnknownAsync returned no task."));

        Assert.Equal(2, proxy.CompleteCalls);
        Assert.Equal(1, proxy.ReadCalls);
    }

    private async Task<(ConversationOperation Operation, ConversationState State)> SubmitAndWaitForTransitionAsync(string suffix)
    {
        var owner = new BrainOwnerId("owner");
        var actor = new ActorId("principal");
        var identity = new ConversationIdentity(owner, actor, "conversation-" + suffix);
        var conversationKey = RuntimeStateKeys.Conversation(owner, actor, identity.ConversationId);
        var conversation = Grain<IConversationNeuron>(conversationKey);
        var now = DateTimeOffset.UtcNow;
        var operationId = "operation-" + suffix;
        var commandId = "command-" + suffix;
        var requestId = "request-" + suffix;
        var accepted = OperationOutboxRecord.Create(
            "accepted-" + suffix,
            operationId,
            InoOperationPhase.Accepted,
            1,
            now,
            identity.ConversationId,
            1,
            requestId,
            conversationKey,
            new OperationFeedView(commandId, string.Empty, false, null, null, null,
                [new OperationFeedTurn(commandId, "user", "return a safe status", InoConversationStates.Queued)]));

        var initialized = await conversation.InitializeAsync(0, identity);
        await conversation.BeginOperationAsync(
            initialized.Revision,
            commandId,
            new string('a', 64),
            operationId,
            "return a safe status",
            requestId,
            new ConversationOutboxEntry(accepted.EventId, "surface-feed", accepted.ToPayloadUtf8(), now, null),
            now);

        await _workflowRunner.FirstCall.WaitAsync(TimeSpan.FromSeconds(10));
        var operation = await WaitForTransitionAsync(conversation, operationId, TimeSpan.FromSeconds(5));
        return (operation, await conversation.ReadAsync());
    }

    private static async Task<ConversationOperation> WaitForTransitionAsync(
        IConversationNeuron conversation,
        string operationId,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var operation = (await conversation.ReadAsync()).Operations.Single(candidate =>
                string.Equals(candidate.OperationId, operationId, StringComparison.Ordinal));
            if (operation.Status is ConversationOperationStatus.Failed or ConversationOperationStatus.RetryScheduled or ConversationOperationStatus.OutcomeUnknown)
                return operation;
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new Xunit.Sdk.XunitException("The workflow did not leave its running state.");
    }

    private static ConversationState RunningState(
        long revision,
        ConversationOperation operation,
        DateTimeOffset now) =>
        new(
            RuntimeStateSchemas.Conversation,
            revision,
            ConversationLifecycle.Active,
            new ConversationIdentity(new BrainOwnerId("owner"), new ActorId("principal"), "conversation-conflict"),
            [new ConversationTurn(1, "user", "run the Feature", now, operation.OperationId, ConversationTurnKind.User, operation.CommandId)],
            [],
            [operation],
            [],
            null,
            null,
            []);

    public class ConflictingConversationProxy : DispatchProxy
    {
        public ConversationState Current { get; set; } = null!;
        public int CompleteCalls { get; private set; }
        public int ReadCalls { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IConversationNeuron.ReadAsync))
            {
                ReadCalls++;
                return Task.FromResult(Current);
            }
            if (targetMethod?.Name == nameof(IConversationNeuron.CompleteWithAssistantAsync))
            {
                CompleteCalls++;
                return CompleteCalls == 1
                    ? Task.FromException<ConversationState>(new RuntimeStateConflictException(4, 5))
                    : Task.FromResult(Current);
            }
            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    private sealed class FailingWorkflowRunner : IAgentWorkflowRunner
    {
        private readonly TaskCompletionSource _firstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Exception? _failure;
        private int _callCount;

        public Task FirstCall => _firstCall.Task;
        public int CallCount => Volatile.Read(ref _callCount);

        public void SetFailure(Exception failure) => _failure = failure;

        public Task<InoWorkflowResult> ExecuteAsync(
            InoWorkflowRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            _firstCall.TrySetResult();
            return Task.FromException<InoWorkflowResult>(_failure ?? new InvalidOperationException("missing failure"));
        }
    }
}
