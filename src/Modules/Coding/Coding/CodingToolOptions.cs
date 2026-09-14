namespace DigitalBrain.Coding;

public sealed record CodingToolOptions(TimeSpan ReactionWait)
{
    public static CodingToolOptions Default { get; } = new(TimeSpan.FromMinutes(2));
}
