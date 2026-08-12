namespace DigitalBrain.Conversations.Mcp;

public sealed record ChatTranscriptPage(string Chat, IReadOnlyList<ChatTranscriptTurn> Turns);

