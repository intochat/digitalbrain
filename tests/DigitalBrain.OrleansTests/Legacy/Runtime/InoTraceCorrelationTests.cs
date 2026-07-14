using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.OrleansTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Hosting;

namespace DigitalBrain.Tests.Runtime;

public sealed class InoTraceCorrelationTests : NeuronTestBase
{
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
                services.AddSingleton<IAgentWorkflowRunner, TraceWorkflowRunner>();
                services.AddSingleton<IInoEffectExecutor, DisabledInoEffectExecutor>();
            });
    }

    protected override void ConfigureClient(IClientBuilder builder) =>
        builder.Configure<ClientMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromSeconds(10));

    [Fact]
    public async Task Worker_trace_carries_durable_request_operation_grain_workflow_and_tool_correlation()
    {
        TraceWorkflowRunner.LastActorScope = null;
        var completed = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == "DigitalBrain.Ino.Worker",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => completed.Enqueue(activity)
        };
        ActivitySource.AddActivityListener(listener);

        var owner = new BrainOwnerId("owner");
        var actor = new ActorId("principal");
        var identity = new ConversationIdentity(owner, actor, "conversation-trace-correlation");
        var conversationGrainKey = RuntimeStateKeys.Conversation(owner, actor, identity.ConversationId);
        var conversation = Grain<IConversationNeuron>(conversationGrainKey);
        var now = DateTimeOffset.UtcNow;
        var operationId = "operation-trace-correlation";
        var requestId = "request-trace-correlation";
        var accepted = OperationOutboxRecord.Create(
            "accepted-trace-correlation",
            operationId,
            InoOperationPhase.Accepted,
            1,
            now,
            identity.ConversationId,
            2,
            requestId,
            conversationGrainKey,
            new OperationFeedView(
                "command-trace-correlation",
                string.Empty,
                false,
                null,
                null,
                null,
                [new OperationFeedTurn(
                    "command-trace-correlation",
                    "user",
                    "read the safe status",
                    InoConversationStates.Queued)]));

        var initialized = await conversation.InitializeAsync(0, identity);
        await conversation.BeginOperationAsync(
            initialized.Revision,
            "command-trace-correlation",
            new string('a', 64),
            operationId,
            "read the safe status",
            requestId,
            new ConversationOutboxEntry("accepted-trace-correlation", "surface-feed", accepted.ToPayloadUtf8(), now, null),
            now);

        var terminal = await WaitForOperationAsync(conversation, operationId, TimeSpan.FromSeconds(12));
        Assert.Equal(ConversationOperationStatus.Failed, terminal.Status);

        var activity = Assert.Single(completed, candidate =>
            candidate.OperationName == "ino.operation.execute" &&
            string.Equals(candidate.GetTagItem("db.ino.operation_id") as string, operationId, StringComparison.Ordinal));
        Assert.Equal(requestId, activity.GetTagItem("db.ino.request_id"));
        Assert.Equal(operationId, activity.GetTagItem("db.ino.operation_id"));
        Assert.Equal(conversationGrainKey, activity.GetTagItem("db.ino.conversation_grain"));
        Assert.Equal("workflow-trace-correlation", activity.GetTagItem("db.ino.workflow_id"));
        Assert.Equal("session-trace-correlation", activity.GetTagItem("db.ino.workflow_session_id"));
        Assert.Equal("safe.read", activity.GetTagItem("db.ino.tool_id"));
        Assert.Equal(RequestScope.Id(owner, actor), TraceWorkflowRunner.LastActorScope);
        Assert.DoesNotContain(activity.TagObjects, tag =>
            tag.Key.Contains("prompt", StringComparison.OrdinalIgnoreCase) ||
            tag.Key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            tag.Key.Contains("payload", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<ConversationOperation> WaitForOperationAsync(
        IConversationNeuron conversation,
        string operationId,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var operation = (await conversation.ReadAsync()).Operations.Single(candidate =>
                string.Equals(candidate.OperationId, operationId, StringComparison.Ordinal));
            if (operation.Status is ConversationOperationStatus.Failed or ConversationOperationStatus.Succeeded)
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

    private sealed class TraceWorkflowRunner : IAgentWorkflowRunner
    {
        public static string? LastActorScope { get; set; }

        public Task<InoWorkflowResult> ExecuteAsync(
            InoWorkflowRequest request,
            CancellationToken cancellationToken = default)
        {
            LastActorScope = request.ActorScope;
            return Task.FromResult(new InoWorkflowResult(
                "The configured read tool cannot run in this test.",
                new WorkflowReference("test", "workflow-trace-correlation", "session-trace-correlation"),
                new InoToolRequest("safe.read", InoToolAccess.Read, "status", "Read the safe status.")));
        }
    }
}
