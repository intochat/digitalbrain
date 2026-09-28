using Microsoft.Extensions.AI;

namespace DigitalBrain.AI.Agents;

// A module contributes agent tools by registering a factory; the context names the trusted scope,
// run and tool call the tools act for.
public sealed record AgentToolContext(string ScopeId, string RunId, string CallId);

public interface IAgentToolFactory
{
    IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context);
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

// Model-safe tool names for neuron methods ("{contract alias}/{method}"): letters, digits and '_' within 64 characters.
public static class NeuronToolName
{
    private const int MaxLength = 64;

    public static string Of(string methodId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodId);
        var name = new string([.. methodId.Select(static character => char.IsAsciiLetterOrDigit(character) ? character : '_')]);
        if (name.Length <= MaxLength) { return name; }
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(methodId)))[..8];
        return name[..(MaxLength - 9)] + "_" + hash;
    }
}
