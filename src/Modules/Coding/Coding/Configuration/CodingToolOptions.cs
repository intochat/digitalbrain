namespace DigitalBrain.Coding;

// ReactionWait bounds how long a code_* tool waits for a change set to settle; EditDeadline bounds the
// Roslyn work inside the settling reaction itself and is the shorter of the two, so a check or a commit
// that cannot finish becomes advice on the snapshot rather than a tool-side "did not settle".
public sealed record CodingToolOptions
{
    public const string SectionName = "DigitalBrain:Coding:Tools";

    public CodingToolOptions() { }

    public CodingToolOptions(TimeSpan reactionWait, TimeSpan editDeadline)
    {
        ReactionWait = reactionWait;
        EditDeadline = editDeadline;
    }

    public TimeSpan ReactionWait { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan EditDeadline { get; set; } = TimeSpan.FromSeconds(90);

    public static CodingToolOptions Default => new();

    public void Deconstruct(out TimeSpan reactionWait, out TimeSpan editDeadline)
    {
        reactionWait = ReactionWait;
        editDeadline = EditDeadline;
    }
}
