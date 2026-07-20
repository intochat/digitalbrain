using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class MafPackageContracts
{
    [Fact(DisplayName = "MAF core owns agent creation, sessions, context and tool approval")]
    public async Task CoreAgentApisCompileAndOwnTheirSessions()
    {
        using var chatClient = new CompileChatClient();
        AIAgent agent = new ChatClientAgent(chatClient, instructions: "compiler contract", name: "contract-agent");
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        var serialized = await agent.SerializeSessionAsync(session, cancellationToken: TestContext.Current.CancellationToken);
        var restored = await agent.DeserializeSessionAsync(serialized, cancellationToken: TestContext.Current.CancellationToken);
        AIContextProvider contextProvider = new CompileContextProvider();
#pragma warning disable MAAI001 // Compile-lock the 1.13 evaluation API while also proving the supported middleware path below.
        AIAgent approval = new ToolApprovalAgent(agent);
        AIAgent approvalPipeline = agent.AsBuilder().UseToolApproval().Build();
        var approvalRequired = new ApprovalRequiredAIFunction(
            AIFunctionFactory.Create((string value) => value));

        Assert.NotNull(restored);
        Assert.NotNull(contextProvider);
        Assert.IsType<ToolApprovalAgent>(approval);
#pragma warning restore MAAI001
        Assert.NotNull(approvalPipeline);
        Assert.NotNull(approvalRequired);
    }

    [Fact(DisplayName = "MAF workflow builders, Lockstep and JSON checkpoint seams compile")]
    public void WorkflowApisCompileAtTheFrozenSeams()
    {
        using var chatClient = new CompileChatClient();
        AIAgent[] participants =
        [
            new ChatClientAgent(chatClient, name: "first"),
            new ChatClientAgent(chatClient, name: "second")
        ];
        var concurrent = AgentWorkflowBuilder.BuildConcurrent(participants);
        var group = AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents))
            .AddParticipants(participants)
            .Build();
        ICheckpointStore<JsonElement> store = new CompileCheckpointStore();
        var checkpoints = CheckpointManager.CreateJson(store);
        Func<Workflow, ChatMessage, CancellationToken, ValueTask<Run>> run =
            (workflow, input, cancellationToken) =>
                InProcessExecution.Lockstep.RunAsync(workflow, input, cancellationToken: cancellationToken);
        Func<Workflow, ChatMessage, CancellationToken, ValueTask<StreamingRun>> stream =
            (workflow, input, cancellationToken) =>
                InProcessExecution.Lockstep.RunStreamingAsync(workflow, input, cancellationToken: cancellationToken);
        var completion = new SuperStepCompletedEvent(
            stepNumber: 1,
            new SuperStepCompletionInfo(activatedExecutors: []) { Checkpoint = default });
        CheckpointInfo? checkpoint = completion.CompletionInfo?.Checkpoint;

        Assert.NotNull(concurrent);
        Assert.NotNull(group);
        Assert.NotNull(InProcessExecution.Lockstep.WithCheckpointing(checkpoints));
        Assert.NotNull(run);
        Assert.NotNull(stream);
        Assert.Null(checkpoint);
    }

    private sealed class CompileContextProvider : AIContextProvider
    {
        protected override ValueTask<AIContext> ProvideAIContextAsync(
            InvokingContext request,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new AIContext());
    }

    private sealed class CompileCheckpointStore : ICheckpointStore<JsonElement>
    {
        public ValueTask<CheckpointInfo> CreateCheckpointAsync(
            string sessionId,
            JsonElement checkpoint,
            CheckpointInfo? parent)
            => throw new NotSupportedException();

        public ValueTask<JsonElement> RetrieveCheckpointAsync(string sessionId, CheckpointInfo checkpoint)
            => throw new NotSupportedException();

        public ValueTask<IEnumerable<CheckpointInfo>> RetrieveIndexAsync(
            string sessionId,
            CheckpointInfo? parent)
            => throw new NotSupportedException();
    }

    private sealed class CompileChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "compiled")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
