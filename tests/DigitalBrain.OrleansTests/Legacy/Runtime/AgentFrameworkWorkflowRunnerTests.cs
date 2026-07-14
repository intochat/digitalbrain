using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests.Runtime;

public sealed class AgentFrameworkWorkflowRunnerTests
{
    [Fact]
    public async Task ExecuteAsync_reuses_the_exact_prior_workflow_reference()
    {
        var chatClient = new EchoChatClient();
        using var services = new ServiceCollection()
            .AddSingleton<IChatClient>(chatClient)
            .BuildServiceProvider();
        var runner = new AgentFrameworkWorkflowRunner(services);
        var prior = new WorkflowReference(
            "agent-framework",
            "agent-framework-operation-1",
            "existing-session");

        var result = await runner.ExecuteAsync(Request(prior));

        Assert.Equal(prior, result.Workflow);
        Assert.Equal(1, chatClient.CallCount);
    }

    [Theory]
    [InlineData("other-runner", "agent-framework-operation-1")]
    [InlineData("agent-framework", "agent-framework-other-operation")]
    public async Task ExecuteAsync_rejects_a_prior_workflow_that_does_not_belong_to_the_runner_or_operation(
        string runnerName,
        string workflowId)
    {
        var chatClient = new EchoChatClient();
        using var services = new ServiceCollection()
            .AddSingleton<IChatClient>(chatClient)
            .BuildServiceProvider();
        var runner = new AgentFrameworkWorkflowRunner(services);
        var prior = new WorkflowReference(runnerName, workflowId, "existing-session");

        await Assert.ThrowsAsync<ArgumentException>(() => runner.ExecuteAsync(Request(prior)));

        Assert.Equal(0, chatClient.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_attaches_no_capability_receipt_when_no_resolver_is_registered()
    {
        var chatClient = new EchoChatClient();
        using var services = new ServiceCollection()
            .AddSingleton<IChatClient>(chatClient)
            .BuildServiceProvider();
        var runner = new AgentFrameworkWorkflowRunner(services);

        var result = await runner.ExecuteAsync(Request());

        Assert.Null(result.Capability);
        Assert.Equal(1, chatClient.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_rejects_a_prior_workflow_without_a_session_reference()
    {
        var chatClient = new EchoChatClient();
        using var services = new ServiceCollection()
            .AddSingleton<IChatClient>(chatClient)
            .BuildServiceProvider();
        var runner = new AgentFrameworkWorkflowRunner(services);
        var prior = new WorkflowReference("agent-framework", "agent-framework-operation-1", string.Empty);

        await Assert.ThrowsAsync<ArgumentException>(() => runner.ExecuteAsync(Request(prior)));

        Assert.Equal(0, chatClient.CallCount);
    }

    private static InoWorkflowRequest Request(WorkflowReference? prior = null) => new(
        "operation-1",
        "conversation-1",
        "Summarize the workspace.",
        [],
        "request-1",
        PriorWorkflow: prior);

    private sealed class EchoChatClient : IChatClient
    {
        public int CallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "safe response"))
            {
                ConversationId = "provider-conversation"
            });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
