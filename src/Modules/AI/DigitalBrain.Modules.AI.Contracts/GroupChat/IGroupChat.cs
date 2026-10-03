using DigitalBrain;
using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.AI.GroupChat;

// Several models discuss one question in turns, building on each other, until they agree or run out of rounds.
[Alias("ai.group-chat"), Orleans.Metadata.DefaultGrainType("ai.group-chat")]
public interface IGroupChat : INeuron
{
    Task<GroupChatState> Configure(GroupChatSetup setup);
    [ResponseTimeout("00:30:00")] Task<GroupChatState> Ask(string question);
    [ReadOnly, AlwaysInterleave] Task<GroupChatState> Read();
}

// Model is the LLM marker name, for example "IGpt56Luna" or "IGemma4".
[GenerateSerializer, Alias("ai.group-chat-participant")]
public sealed record GroupChatParticipant(
    [property: Id(0)] string Name,
    [property: Id(1)] string Model,
    [property: Id(2)] string Instructions = "");

// The first participant also moderates: it writes the final answer.
[GenerateSerializer, Alias("ai.group-chat-setup")]
public sealed record GroupChatSetup(
    [property: Id(0)] GroupChatParticipant[] Participants,
    [property: Id(1)] string Brief = "",
    [property: Id(2)] int MaxRounds = 3);

[GenerateSerializer, Alias("ai.group-chat-turn")]
public sealed record GroupChatTurn(
    [property: Id(0)] int Round,
    [property: Id(1)] string Speaker,
    [property: Id(2)] string Text);

public enum GroupChatStatus { Idle, Discussing, Concluded, Failed }

[GenerateSerializer, Alias("ai.group-chat-state")]
public sealed record GroupChatState
{
    [Id(0)] public long Revision { get; init; }
    [Id(1)] public GroupChatSetup? Setup { get; init; }
    [Id(2)] public string Question { get; init; } = "";
    [Id(3)] public GroupChatTurn[] Turns { get; init; } = [];
    [Id(4)] public string Answer { get; init; } = "";
    [Id(5)] public GroupChatStatus Status { get; init; }
    [Id(6)] public bool Agreed { get; init; }
    [Id(7)] public string Failure { get; init; } = "";
}
