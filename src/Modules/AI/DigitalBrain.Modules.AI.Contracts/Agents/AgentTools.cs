using Microsoft.Extensions.AI;

namespace DigitalBrain.AI.Agents;

// A module contributes agent tools by registering a factory; the context names the trusted scope,
// run and tool call the tools act for.
public sealed record AgentToolContext(string ScopeId, string RunId, string CallId)
{
    public string? DatabaseSource { get; init; }
}

public interface IAgentToolFactory
{
    IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context);
}

public interface IAgentToolSource
{
    Task<IAgentToolSession> OpenAsync(IReadOnlyList<string> selectedToolNames,
        Func<AgentToolContext> context, CancellationToken ct);
}

// Owns live tool connections until the execution completes.
public interface IAgentToolSession : IAsyncDisposable
{
    IReadOnlyList<AIFunction> Tools { get; }
}

public sealed record AgentContextRequest(string ScopeId, string Message, string? PreviousMessage = null);

// Text is what the model should know before it answers; Tools are registered tools it may need for it.
public sealed record AgentContext(string? Text, IReadOnlyList<string> Tools)
{
    public static AgentContext None { get; } = new(null, []);
}

public interface IAgentContextProvider
{
    string Name { get; }
    Task<AgentContext> Provide(AgentContextRequest request, CancellationToken ct);
}

// A tool result that also offers more registered tools to the rest of the turn; the model sees Result.
public sealed record AgentToolOffer(IReadOnlyList<string> Tools, object? Result);
