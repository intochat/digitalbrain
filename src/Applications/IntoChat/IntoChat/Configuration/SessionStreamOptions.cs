namespace IntoChat;

public sealed class SessionStreamOptions
{
    public const string SectionName = "IntoChat:SessionStream";

    // IntoChat:SessionStream:PollInterval defaults to 100 ms and must be positive.
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(100);
}
