using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Simulation.Tests;

// A tool source a module would register through IAgentToolSource: proves the seam
// carries a tool from DI registration through to the model call's ChatOptions.Tools.
internal sealed class ProbeToolSource : IAgentToolSource
{
    public ValueTask<IReadOnlyList<AITool>> GetToolsAsync(AgentToolContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult<IReadOnlyList<AITool>>(
            [AIFunctionFactory.Create(ProbeAsync, new AIFunctionFactoryOptions { Name = "probe_tool" })]);

    private static Task<string> ProbeAsync() => Task.FromResult("probed");
}

// Stands in for a provider IChatClient: records the ChatOptions the Agent built so the
// test can assert on what actually reached the model call, then returns a canned reply
// so the chat turn still completes.
internal sealed class CapturingChatClient : IChatClient
{
    private const string Reply = "Captured assistant reply.";

    public ChatOptions? LastOptions { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        LastOptions = options;
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Reply)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LastOptions = options;
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, Reply)
        {
            FinishReason = ChatFinishReason.Stop,
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
