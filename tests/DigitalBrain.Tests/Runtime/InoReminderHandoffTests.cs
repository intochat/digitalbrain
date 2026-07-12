using System.Diagnostics;
using System.Text;
using System.Threading;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Hosting;

namespace DigitalBrain.Tests.Runtime;

public sealed class InoReminderHandoffTests : NeuronTestBase
{
    private static int _workflowCalls;

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
                services.AddSingleton<IAgentWorkflowRunner, SucceedingWorkflowRunner>();
                services.AddSingleton<IInoToolGateway, ClosedInoToolGateway>();
            });
    }

    protected override void ConfigureClient(IClientBuilder builder) =>
        builder.Configure<ClientMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromSeconds(10));

    [Fact]
    public async Task Conversation_reminder_hands_off_to_worker_reminder_and_completes_the_operation()
    {
        Interlocked.Exchange(ref _workflowCalls, 0);
        var tenant = new TenantId("tenant");
        var workspace = new WorkspaceId("workspace");
        var principal = new PrincipalRef("principal", PrincipalKind.User);
        var identity = new ConversationIdentity(
            tenant,
            workspace,
            principal,
            "conversation-reminder-handoff");
        var conversation = Grain<IConversationNeuron>(RuntimeStateKeys.Conversation(
            tenant,
            workspace,
            principal,
            identity.ConversationId));
        var now = DateTimeOffset.UtcNow;
        var acceptedEventId = "accepted-reminder-handoff";
        var acceptedProjection = OperationOutboxRecord.Create(
            acceptedEventId,
            "operation-reminder-handoff",
            InoOperationPhase.Accepted,
            1,
            now,
            identity.ConversationId,
            2,
            "request-reminder-handoff",
            RuntimeStateKeys.Conversation(tenant, workspace, principal, identity.ConversationId),
            new OperationFeedView(
                "command-reminder-handoff",
                string.Empty,
                false,
                null,
                null,
                null,
                [new OperationFeedTurn(
                    "command-reminder-handoff",
                    "user",
                    "summarize the status",
                    InoConversationStates.Queued)]));

        var initialized = await conversation.InitializeAsync(0, identity);
        await conversation.BeginOperationAsync(
            initialized.Revision,
            "command-reminder-handoff",
            new string('a', 64),
            "operation-reminder-handoff",
            "summarize the status",
            "request-reminder-handoff",
            new ConversationOutboxEntry(acceptedEventId, "surface-feed", acceptedProjection.ToPayloadUtf8(), now, null),
            now);

        var completed = await WaitForOperationAsync(
            conversation,
            "operation-reminder-handoff",
            ConversationOperationStatus.Succeeded,
            TimeSpan.FromSeconds(12));

        Assert.Equal(ConversationOperationStatus.Succeeded, completed.Status);
        Assert.True(
            Volatile.Read(ref _workflowCalls) == 1,
            $"A reminder handoff must execute one claimed workflow; calls={Volatile.Read(ref _workflowCalls)}, attempt={completed.Attempt}, version={completed.Version}.");
    }

    [Fact]
    public async Task Outbox_dispatcher_leaves_a_noncanonical_surface_feed_payload_pending_without_reordering_later_phases()
    {
        Interlocked.Exchange(ref _workflowCalls, 0);
        var tenant = new TenantId("tenant");
        var workspace = new WorkspaceId("workspace");
        var principal = new PrincipalRef("principal", PrincipalKind.User);
        var identity = new ConversationIdentity(
            tenant,
            workspace,
            principal,
            "conversation-noncanonical-outbox");
        var conversationKey = RuntimeStateKeys.Conversation(
            tenant,
            workspace,
            principal,
            identity.ConversationId);
        var conversation = Grain<IConversationNeuron>(conversationKey);
        var malformedOutboxId = "noncanonical-outbox";
        var now = DateTimeOffset.UtcNow;

        var initialized = await conversation.InitializeAsync(0, identity);
        await conversation.BeginOperationAsync(
            initialized.Revision,
            "command-noncanonical-outbox",
            new string('b', 64),
            "operation-noncanonical-outbox",
            "summarize the status",
            "request-noncanonical-outbox",
            new ConversationOutboxEntry(
                malformedOutboxId,
                "surface-feed",
                Encoding.UTF8.GetBytes("{\"EventId\":\"noncanonical-outbox\"}"),
                now,
                null),
            now);
        await Grain<IInoConversationOutboxDispatcherGrain>(conversationKey).ScheduleAsync();

        await WaitForOperationAsync(
            conversation,
            "operation-noncanonical-outbox",
            ConversationOperationStatus.Succeeded,
            TimeSpan.FromSeconds(12));
        var state = await conversation.ReadAsync();

        Assert.Null(state.Outbox.Single(entry =>
            string.Equals(entry.OutboxId, malformedOutboxId, StringComparison.Ordinal)).DispatchedAt);
        Assert.All(state.Outbox, entry => Assert.Null(entry.DispatchedAt));
    }

    private static async Task<ConversationOperation> WaitForOperationAsync(
        IConversationNeuron conversation,
        string operationId,
        ConversationOperationStatus expectedStatus,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var operation = (await conversation.ReadAsync()).Operations.Single(candidate =>
                string.Equals(candidate.OperationId, operationId, StringComparison.Ordinal));
            if (operation.Status == expectedStatus) return operation;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        var final = (await conversation.ReadAsync()).Operations.Single(candidate =>
            string.Equals(candidate.OperationId, operationId, StringComparison.Ordinal));
        throw new Xunit.Sdk.XunitException(
            $"Operation {operationId} did not reach {expectedStatus}; final state was {final.Status}.");
    }

    private static async Task<ConversationState> WaitForStateAsync(
        IConversationNeuron conversation,
        Func<ConversationState, bool> condition,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var state = await conversation.ReadAsync();
            if (condition(state)) return state;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new Xunit.Sdk.XunitException("The expected durable outbox state was not reached.");
    }

    private sealed class SucceedingWorkflowRunner : IAgentWorkflowRunner
    {
        public Task<InoWorkflowResult> ExecuteAsync(
            InoWorkflowRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _workflowCalls);
            return Task.FromResult(new InoWorkflowResult(
                "The status is ready.",
                new WorkflowReference("test", "workflow", "session")));
        }
    }
}
