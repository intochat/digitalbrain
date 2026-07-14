using System.Diagnostics;
using DigitalBrain.Kernel.Contracts.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
namespace DigitalBrain.Kernel.Runtime;

internal sealed class AgentFrameworkWorkflowRunner(IServiceProvider services) : IAgentWorkflowRunner
{
    private const string RunnerName = "agent-framework";
    private static readonly ActivitySource ActivitySource = new("DigitalBrain.Ino.Workflow");
    public async Task<InoWorkflowResult> ExecuteAsync(InoWorkflowRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OperationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);
        var workflow = ResolveWorkflowReference(request);
        using var activity = ActivitySource.StartActivity("ino.workflow.execute", ActivityKind.Internal);
        activity?.SetTag("db.ino.operation_id", request.OperationId);
        activity?.SetTag("db.ino.workflow_id", workflow.WorkflowId);
        activity?.SetTag("db.ino.request_id", request.RequestId);
        var chatClient = services.GetService<IChatClient>()
            ?? throw new InvalidOperationException("INO requires a configured Microsoft.Extensions.AI chat client.");
        var agent = new ChatClientAgent(
            chatClient,
            instructions: "You are INO, a concise workspace assistant. Never expose credentials, tokens, raw provider payloads, internal identifiers, or infrastructure details.",
            name: "ino");
        var session = await agent.CreateSessionAsync(workflow.SessionId, cancellationToken).ConfigureAwait(false);
        var messages = request.History.TakeLast(12).Select(static history => new ChatMessage(ChatRole.User, history))
            .Append(new ChatMessage(ChatRole.User, request.Prompt))
            .ToArray();
        var response = await agent.RunAsync(messages, session, options: null, cancellationToken: cancellationToken).ConfigureAwait(false);
        var text = response.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("The workflow returned an empty response.");
        return new InoWorkflowResult(text, workflow);
    }
    private static WorkflowReference ResolveWorkflowReference(InoWorkflowRequest request)
    {
        var workflowId = RunnerName + "-" + request.OperationId;
        if (request.PriorWorkflow is not { } prior)
            return new WorkflowReference(RunnerName, workflowId, Guid.NewGuid().ToString("N"));
        if (!string.Equals(prior.Runner, RunnerName, StringComparison.Ordinal) ||
            !string.Equals(prior.WorkflowId, workflowId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(prior.SessionId))
            throw new ArgumentException("The prior workflow does not belong to this INO operation.", nameof(request));
        return prior;
    }
}
