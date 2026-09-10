namespace DigitalBrain.Google;

[GenerateSerializer]
[Alias("db.gmail.connection")]
public sealed record GmailConnection(
    [property: Id(0)] bool Connected,
    [property: Id(1)] string? Email,
    [property: Id(2)] bool CanCompose,
    [property: Id(3)] DateTimeOffset? ExpiresAt);
