namespace DigitalBrain.Coding;

// ReactionWait bounds how long a code_* tool waits for a change set to settle; EditDeadline bounds the
// Roslyn work inside the settling reaction itself and is the shorter of the two, so a check or a commit
// that cannot finish becomes advice on the snapshot rather than a tool-side "did not settle".
public sealed record CodingToolOptions(TimeSpan ReactionWait, TimeSpan EditDeadline)
{
    public static CodingToolOptions Default { get; } = new(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(90));
}
