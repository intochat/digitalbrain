namespace DigitalBrain.Google;

[GenerateSerializer]
[Alias("db.google.gmail-message-header")]
public sealed record GmailMessageHeader(
    [property: Id(0)] string Id,
    [property: Id(1)] string Subject,
    [property: Id(2)] string Sender);
