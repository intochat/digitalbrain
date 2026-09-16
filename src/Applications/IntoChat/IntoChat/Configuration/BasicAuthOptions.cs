namespace IntoChat;

public sealed class BasicAuthOptions
{
    public const string SectionName = "DigitalBrain:Auth";
    public string? Username { get; set; }
    public string? Password { get; set; }
}
