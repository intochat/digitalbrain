using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.AI.Agents;

[Alias("agent"), Orleans.Metadata.DefaultGrainType("agent")]
public interface IAgent : INeuron
{
    [AlwaysInterleave, ResponseTimeout("00:10:00")] Task<AgentReply> Ask(AgentRequest request);
    [AlwaysInterleave] Task<ModelDescriptor> GetCapabilities(CancellationToken ct = default);
    [AlwaysInterleave] Task Configure(AgentDefinition definition, long expectedRevision, CancellationToken ct = default);
    [AlwaysInterleave, ResponseTimeout("00:10:00")] Task<string> GetResponse(string prompt, CancellationToken ct = default);
    [AlwaysInterleave, ResponseTimeout("00:10:00")] Task<AgentResponse> GetRichResponse(AiMessage message, CancellationToken ct = default);
    [AlwaysInterleave, ResponseTimeout("00:10:00")] Task<AgentResponse> GetRichResponse(string prompt, CancellationToken ct = default);
    [AlwaysInterleave, ResponseTimeout("00:10:00")] IAsyncEnumerable<string> GetResponseStream(string prompt, CancellationToken ct = default);
    [AlwaysInterleave, ResponseTimeout("00:10:00")] IAsyncEnumerable<string> GetResponseStream(AiMessage message, CancellationToken ct = default);
    [AlwaysInterleave] Task<IReadOnlyList<AiMessage>> GetHistory(CancellationToken ct = default);
    [AlwaysInterleave] Task ClearHistory(CancellationToken ct = default);
    [AlwaysInterleave] Task<AgentState> GetState(CancellationToken ct = default);
    [AlwaysInterleave] Task<AgentMetadata> GetMetadata(CancellationToken ct = default);
    [AlwaysInterleave] Task<AgentUsage?> GetLastUsage(CancellationToken ct = default);
    [AlwaysInterleave] Task<IReadOnlyList<AgentEvent>> GetEventLog(CancellationToken ct = default);
    [AlwaysInterleave] Task Cancel(CancellationToken ct = default);
}