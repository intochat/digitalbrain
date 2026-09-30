using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DigitalBrain.AI.Agents;

public sealed record AgentTurnRequest(string AgentId, string RunId, string ScopeId,
    IReadOnlyList<AgentConversationTurn> History, string Message, AgentModelSelection? Model,
    string? Instructions = null, IReadOnlyList<string>? ToolNames = null,
    IReadOnlyList<AiMessage>? Messages = null, AiMessage? Input = null,
    bool Streaming = false, int MaxModelCalls = 16, TimeSpan? Timeout = null, InferenceOptions? Options = null,
    IReadOnlyList<string>? ContextProviders = null);
