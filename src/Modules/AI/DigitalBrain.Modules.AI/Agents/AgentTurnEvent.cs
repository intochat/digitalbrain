using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI.Agents;

public abstract record AgentTurnEvent
{
    internal TaskCompletionSource? Observed { get; set; }
    public sealed record Started(string RunId) : AgentTurnEvent;
    public sealed record ModelSelected(ResolvedAgentModel Model, ModelDescriptor Descriptor) : AgentTurnEvent;
    public sealed record Text(string Content) : AgentTurnEvent;
    public sealed record ToolStarted(string CallId, string Name, string Arguments) : AgentTurnEvent;
    public sealed record ToolCompleted(string CallId, string Name, string Result) : AgentTurnEvent;
    public sealed record ToolFailed(string CallId, string Name) : AgentTurnEvent;
    public sealed record Completed(IReadOnlyList<AiMessage> Messages, AgentUsage? Usage) : AgentTurnEvent;
    public sealed record Finished : AgentTurnEvent;
    public sealed record Failed(string Message, bool Cancelled = false) : AgentTurnEvent;
}
