using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.AI;

[Alias("agent-builder")]
public interface IAgentBuilder : INeuron
{
    [Alias("build")]
    Task<IAgent> Build(BuildAgent command);

    [ReadOnly, Alias("read")]
    Task<IReadOnlyList<AgentBuildSnapshot>> Read();

    [Alias("close")]
    Task Close(CloseAgentBuilder command);
}

// Durable conversation operations. Awaiting and observation live in AgentConversation
// so callers never hold the owning neuron's turn while its worker completes.
[Alias("agent")]
public interface IAgent : INeuron
{
    [ReadOnly, Alias("read")]
    Task<AgentSnapshot> GetState();

    [Alias("send")]
    Task<AgentResponse> Submit(AgentRequest request);

    [ReadOnly, Alias("response")]
    Task<AgentResponse?> GetResponse(string requestId);

    // User/assistant text retained as conversation context; excludes internal tool traffic.
    [ReadOnly, Alias("history")]
    Task<IReadOnlyList<AgentMessage>> GetHistory();

    // Clears future model context, not the durable request/result ledger. Requires an idle agent.
    [Alias("clear-history")]
    Task ClearHistory();

    // A null TaskId retires the agent; a specific TaskId cancels only that request.
    [Alias("stop")]
    Task<AgentSnapshot> Cancel(CancelAgent request);
}

[GenerateSerializer, Alias("db.ai.message")]
public sealed record AgentMessage(
    [property: Id(0)] string Role,
    [property: Id(1)] string Text);

[GenerateSerializer, Alias("db.ai.build-agent")]
public sealed record BuildAgent(
    CommandId Id,
    [property: Id(0)] string Key,
    [property: Id(1)] string Instructions,
    [property: Id(2)] AgentModelSelection? Model = null,
    [property: Id(3)] IReadOnlyList<string>? Tools = null,
    [property: Id(4)] string? InitialMessage = null,
    [property: Id(5)] string? Name = null,
    [property: Id(6)] string? Owner = null,
    [property: Id(7)] bool Retain = false) : Command(Id);

[GenerateSerializer, Alias("db.ai.send-agent")]
public sealed record AgentRequest(CommandId Id, [property: Id(0)] string Message,
    [property: Id(1)] string? TaskId = null) : Command(Id);

[GenerateSerializer, Alias("db.ai.stop-agent")]
public sealed record CancelAgent(CommandId Id, [property: Id(0)] string? TaskId = null) : Command(Id);

[GenerateSerializer, Alias("db.ai.close-agent-builder")]
public sealed record CloseAgentBuilder(CommandId Id) : Command(Id);

[GenerateSerializer, Alias("db.ai.agent-build-snapshot")]
public sealed record AgentBuildSnapshot(
    [property: Id(0)] string Key,
    [property: Id(1)] NeuronId AgentId,
    [property: Id(2)] string Name,
    [property: Id(3)] string Status,
    [property: Id(4)] bool Retained,
    [property: Id(5)] DateTimeOffset CreatedAt);

[GenerateSerializer, Alias("db.ai.agent-snapshot")]
public sealed record AgentSnapshot(
    [property: Id(0)] NeuronId AgentId,
    [property: Id(1)] string Name,
    [property: Id(2)] string Status,
    [property: Id(3)] ResolvedAgentModel Model,
    [property: Id(4)] string Instructions,
    [property: Id(5)] IReadOnlyList<string> Tools,
    [property: Id(6)] string? Owner,
    [property: Id(7)] IReadOnlyList<AgentResponse> Tasks,
    [property: Id(8)] DateTimeOffset CreatedAt,
    [property: Id(9)] string? InitialTaskId,
    [property: Id(10)] bool Retained);

[GenerateSerializer, Alias("db.ai.agent-task-snapshot")]
public sealed record AgentResponse(
    [property: Id(0)] string TaskId,
    [property: Id(1)] string Status,
    [property: Id(2)] string Prompt,
    [property: Id(3)] string? Output,
    [property: Id(4)] string? Error,
    [property: Id(5)] DateTimeOffset QueuedAt,
    [property: Id(6)] DateTimeOffset? StartedAt,
    [property: Id(7)] DateTimeOffset? CompletedAt);
