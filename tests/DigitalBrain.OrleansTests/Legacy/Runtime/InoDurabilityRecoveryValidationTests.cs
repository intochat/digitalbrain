extern alias McpProject;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.OrleansTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using ConversationStateClient = McpProject::DigitalBrain.Mcp.ConversationStateClient;
using McpInoCommandHandler = McpProject::DigitalBrain.Mcp.McpInoCommandHandler;
using RuntimeRequestContext = DigitalBrain.Kernel.Contracts.Runtime.RequestContext;

namespace DigitalBrain.Tests.Runtime;

public sealed class InoDurabilityRecoveryValidationTests : NeuronTestBase
{
    private const string TraceRequestId = "request-dispatcher-presentation-trace";
    private static readonly ConcurrentDictionary<string, int> WorkflowCalls = new(StringComparer.Ordinal);

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
                services.AddSingleton<IAgentWorkflowRunner, ValidationWorkflowRunner>();
                services.AddSingleton<IInoEffectExecutor, DisabledInoEffectExecutor>();
            });
    }

    protected override void ConfigureClient(IClientBuilder builder) =>
        builder.Configure<ClientMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromSeconds(10));

    [Fact]
    public async Task Accepted_command_persists_before_a_client_disconnect_and_completes_without_the_client()
    {
        WorkflowCalls.Clear();
        var context = Context("request-client-disconnect");
        var receipt = await AcceptAsync(context, "command-client-disconnect", "summarize the status");
        var conversation = Conversation(context);
        var accepted = await conversation.ReadAsync();

        var acceptedCommand = Assert.Single(accepted.AcceptedCommands);
        var acceptedOperation = Assert.Single(accepted.Operations);
        var acceptedOutbox = RequiredOutbox(accepted, receipt.OperationId, InoOperationPhase.Accepted);
        Assert.Equal(receipt.OperationId, acceptedCommand.OperationId);
        Assert.Equal(receipt.OperationId, acceptedOperation.OperationId);
        Assert.Equal(context.CorrelationId, acceptedCommand.RequestId);
        Assert.Equal(context.CorrelationId, acceptedOutbox.Record.RequestId);
        Assert.Null(acceptedOutbox.Entry.DispatchedAt);

        using var requestAborted = new CancellationTokenSource();
        requestAborted.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Task.Delay(Timeout.InfiniteTimeSpan, requestAborted.Token));

        var completed = await WaitForOperationAsync(
            conversation,
            receipt.OperationId,
            ConversationOperationStatus.Succeeded,
            TimeSpan.FromSeconds(12));

        Assert.Equal(ConversationOperationStatus.Succeeded, completed.Status);
        Assert.Equal(1, WorkflowCallCount(context.CorrelationId));
    }

    [Fact]
    public async Task Reminder_rehydrates_after_conversation_worker_and_dispatcher_deactivation_and_completes_once()
    {
        WorkflowCalls.Clear();
        var context = Context("request-grain-restart");
        var receipt = await AcceptAsync(context, "command-grain-restart", "summarize the status");
        var conversationKey = ConversationKey(context);
        var conversation = Conversation(context);

        await Cluster.DeactivateAsync((IAddressable)conversation);
        await Cluster.DeactivateAsync((IAddressable)Grain<IInoOperationWorkerGrain>(
            conversationKey + "|" + receipt.OperationId));
        await Cluster.DeactivateAsync((IAddressable)Grain<IInoConversationOutboxDispatcherGrain>(conversationKey));

        var rehydrated = Conversation(context);
        var recovered = await rehydrated.ReadAsync();
        Assert.Contains(recovered.AcceptedCommands, command => command.OperationId == receipt.OperationId);
        Assert.Contains(recovered.Operations, operation => operation.OperationId == receipt.OperationId);
        Assert.Equal(0, WorkflowCallCount(context.CorrelationId));

        var completed = await WaitForOperationAsync(
            rehydrated,
            receipt.OperationId,
            ConversationOperationStatus.Succeeded,
            TimeSpan.FromSeconds(12));

        Assert.Equal(ConversationOperationStatus.Succeeded, completed.Status);
        Assert.Equal(1, WorkflowCallCount(context.CorrelationId));
    }

    [Fact]
    public async Task Worker_dispatcher_and_presentation_keep_durable_correlation()
    {
        WorkflowCalls.Clear();
        var workerActivities = new ConcurrentQueue<Activity>();
        var dispatcherActivities = new ConcurrentQueue<Activity>();
        using var workerListener = Listener("DigitalBrain.Ino.Worker", workerActivities);
        using var dispatcherListener = Listener("DigitalBrain.Ino.Outbox", dispatcherActivities);
        ActivitySource.AddActivityListener(workerListener);
        ActivitySource.AddActivityListener(dispatcherListener);

        var context = Context(TraceRequestId);
        var receipt = await AcceptAsync(context, "command-dispatcher-presentation-trace", "read the safe status");
        var conversationKey = ConversationKey(context);
        var conversation = Conversation(context);

        var terminal = await WaitForOperationAsync(
            conversation,
            receipt.OperationId,
            ConversationOperationStatus.Failed,
            TimeSpan.FromSeconds(12));
        Assert.Equal(ConversationOperationStatus.Failed, terminal.Status);

        var terminalOutbox = RequiredOutbox(
            await conversation.ReadAsync(),
            receipt.OperationId,
            InoOperationPhase.Failed);
        Assert.Equal("safe.read", terminalOutbox.Record.ToolId);
        var feed = Grain<ISurfaceFeedNeuron>(RuntimeStateKeys.SurfaceFeed(
            context.OwnerId,
            context.ActorId));
        var presentation = await WaitForProjectionAsync(feed, terminalOutbox.Entry.OutboxId, TimeSpan.FromSeconds(12));

        Assert.Contains(presentation.EventHistory, record =>
            string.Equals(record.ProjectionId, terminalOutbox.Entry.OutboxId, StringComparison.Ordinal));
        var worker = await WaitForActivityAsync(workerActivities, activity =>
            activity.OperationName == "ino.operation.execute" &&
            string.Equals(activity.GetTagItem("db.ino.operation_id") as string, receipt.OperationId, StringComparison.Ordinal),
            TimeSpan.FromSeconds(3));
        var dispatcher = await WaitForActivityAsync(dispatcherActivities, activity =>
            activity.OperationName == "ino.outbox.dispatch" &&
            string.Equals(activity.GetTagItem("db.ino.operation_id") as string, receipt.OperationId, StringComparison.Ordinal) &&
            string.Equals(activity.GetTagItem("db.ino.workflow_id") as string, "workflow-dispatcher-presentation-trace", StringComparison.Ordinal),
            TimeSpan.FromSeconds(3));

        Assert.Equal(TraceRequestId, worker.GetTagItem("db.ino.request_id"));
        Assert.Equal(receipt.OperationId, worker.GetTagItem("db.ino.operation_id"));
        Assert.Equal(conversationKey, worker.GetTagItem("db.ino.conversation_grain"));
        Assert.Equal("workflow-dispatcher-presentation-trace", worker.GetTagItem("db.ino.workflow_id"));
        Assert.Equal("session-dispatcher-presentation-trace", worker.GetTagItem("db.ino.workflow_session_id"));
        Assert.Equal("safe.read", worker.GetTagItem("db.ino.tool_id"));

        Assert.Equal(TraceRequestId, dispatcher.GetTagItem("db.request.id"));
        Assert.Equal(receipt.OperationId, dispatcher.GetTagItem("db.ino.operation_id"));
        Assert.Equal(conversationKey, dispatcher.GetTagItem("db.ino.conversation_grain"));
        Assert.Equal("workflow-dispatcher-presentation-trace", dispatcher.GetTagItem("db.ino.workflow_id"));
        Assert.Equal("session-dispatcher-presentation-trace", dispatcher.GetTagItem("db.ino.workflow_session_id"));
        Assert.Equal("safe.read", dispatcher.GetTagItem("db.ino.tool_id"));
    }

    private async Task<OperationReceipt> AcceptAsync(
        RuntimeRequestContext context,
        string commandId,
        string prompt) => await new McpInoCommandHandler(
            new ConversationStateClient(Cluster.Client, TimeProvider.System))
        .AcceptAsync(new CommandEnvelope(
            McpInoCommandHandler.CommandType,
            1,
            commandId,
            context,
            JsonSerializer.SerializeToElement(new { prompt })));

    private IConversationNeuron Conversation(RuntimeRequestContext context) =>
        Grain<IConversationNeuron>(ConversationKey(context));

    private static string ConversationKey(RuntimeRequestContext context) => RuntimeStateKeys.Conversation(
        context.OwnerId,
        context.ActorId,
        InoConversationIdentity.From(context));

    private static RuntimeRequestContext Context(string requestId) => new(
        new BrainOwnerId("owner"),
        new ActorId("principal"),
        new SessionId("session-" + requestId),
        AuthAssurance.Oidc,
        requestId,
        null,
        new HashSet<string>(StringComparer.Ordinal));

    private static (ConversationOutboxEntry Entry, OperationOutboxRecord Record) RequiredOutbox(
        ConversationState state,
        string operationId,
        InoOperationPhase phase)
    {
        foreach (var entry in state.Outbox)
        {
            if (OperationOutboxRecord.TryRead(entry.PayloadUtf8, out var record) && record is not null &&
                string.Equals(record.OperationId, operationId, StringComparison.Ordinal) && record.Phase == phase)
                return (entry, record);
        }

        throw new Xunit.Sdk.XunitException($"No {phase} outbox record was persisted for {operationId}.");
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

    private static async Task<SurfaceFeedState> WaitForProjectionAsync(
        ISurfaceFeedNeuron feed,
        string projectionId,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var state = await feed.ReadAsync();
            if (state.EventHistory.Any(record => string.Equals(record.ProjectionId, projectionId, StringComparison.Ordinal)))
                return state;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new Xunit.Sdk.XunitException($"The durable feed did not receive projection {projectionId}.");
    }

    private static async Task<Activity> WaitForActivityAsync(
        ConcurrentQueue<Activity> activities,
        Func<Activity, bool> predicate,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var activity = activities.FirstOrDefault(predicate);
            if (activity is not null) return activity;
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new Xunit.Sdk.XunitException("The expected durable trace activity was not recorded.");
    }

    private static ActivityListener Listener(string sourceName, ConcurrentQueue<Activity> activities) => new()
    {
        ShouldListenTo = source => source.Name == sourceName,
        Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        ActivityStopped = activities.Enqueue
    };

    private static int WorkflowCallCount(string requestId) =>
        WorkflowCalls.TryGetValue(requestId, out var calls) ? calls : 0;

    private sealed class ValidationWorkflowRunner : IAgentWorkflowRunner
    {
        public Task<InoWorkflowResult> ExecuteAsync(
            InoWorkflowRequest request,
            CancellationToken cancellationToken = default)
        {
            WorkflowCalls.AddOrUpdate(request.RequestId, 1, static (_, calls) => checked(calls + 1));
            var suffix = request.RequestId["request-".Length..];
            var workflow = new WorkflowReference("test", "workflow-" + suffix, "session-" + suffix);
            if (string.Equals(request.RequestId, TraceRequestId, StringComparison.Ordinal))
            {
                return Task.FromResult(new InoWorkflowResult(
                    "The configured read tool cannot run in this test.",
                    workflow,
                    new InoToolRequest("safe.read", InoToolAccess.Read, "status", "Read the safe status.")));
            }

            return Task.FromResult(new InoWorkflowResult("The status is ready.", workflow));
        }
    }
}
