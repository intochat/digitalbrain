using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.AI.Conversations;

[Alias("ai.conversation"), Orleans.Metadata.DefaultGrainType("ai.conversation")]
public interface IConversation : INeuron
{
    Task<ConversationState> Begin(string runId, string message, long expectedRevision, string runtimeId);
    Task<ConversationState> Complete(ConversationTurn turn);
    Task<ConversationState> Interrupt(string runId);
    [ReadOnly] Task<ConversationState> Read();
}

[GenerateSerializer, Alias("ai.conversation-turn")]
public sealed record ConversationTurn(
    [property: Id(0)] string RunId, [property: Id(1)] string UserText,
    [property: Id(2)] string AssistantText, [property: Id(3)] IReadOnlyList<string> ResultIds);

[GenerateSerializer, Alias("ai.conversation-state")]
public sealed record ConversationState(
    [property: Id(0)] long Revision, [property: Id(1)] string? ActiveRunId,
    [property: Id(2)] IReadOnlyList<ConversationTurn> Turns);
