using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Chat;

namespace DigitalBrain.UI;

// Newest last, bounded because the chat outlives the turns it handles.
[GenerateSerializer, Alias("db.ui.chat-state")]
public sealed record ChatState(
    [property: Id(0)] List<ChatTurnRecord> Turns,
    [property: Id(1)] List<ChatTurn> Transcript,
    [property: Id(2)] List<TurnContext> Contexts,
    [property: Id(3)] NeuronId? Agent,
    [property: Id(4)] long Version,
    [property: Id(5)] List<CommandTurn> TurnByCommand)
{
    public const int MaxTurns = 64;
    public const int MaxTranscript = 500;
    public const int MaxInlineContexts = 32;
    public const int MaxCommandTurns = 1024;
}
