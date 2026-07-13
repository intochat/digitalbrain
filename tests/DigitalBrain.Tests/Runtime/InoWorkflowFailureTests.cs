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
                services.AddSingleton<IInoToolGateway, ClosedInoToolGateway>();
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

    private async Task<(ConversationOperation Operation, ConversationState State)> SubmitAndWaitForTransitionAsync(string suffix)
    {
        var tenant = new TenantId("tenant");
        var workspace = new WorkspaceId("workspace");
        var principal = new PrincipalRef("principal", PrincipalKind.User);
        var identity = new ConversationIdentity(tenant, workspace, principal, "conversation-" + suffix);
        var conversationKey = RuntimeStateKeys.Conversation(tenant, workspace, principal, identity.ConversationId);
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
            if (operation.Status is ConversationOperationStatus.Failed or ConversationOperationStatus.RetryScheduled)
                return operation;
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new Xunit.Sdk.XunitException("The workflow did not leave its running state.");
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
