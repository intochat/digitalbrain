namespace DigitalBrain.Platform.Identity.Configuration;

// Open: single-tenant development and test — anonymous requests act as the synthetic "owner".
// Secured: cookie sessions plus an optional Basic bootstrap credential — anonymous gets 401.
public enum IdentityPosture
{
    Open,
    Secured,
}

// The host declares its security posture; it is never inferred from what a request carries or
// which keys happen to be configured, and a host that declares nothing refuses to start.
public sealed class AuthOptions
{
    public const string SectionName = "DigitalBrain:Auth";
    public const string PostureKey = "DigitalBrain:Auth:Posture";

    public IdentityPosture? Posture { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}
