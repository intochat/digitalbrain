namespace DigitalBrain.Mcp;

internal sealed record ChatTranscriptPage(string Chat, IReadOnlyList<ChatTranscriptTurn> Turns);

