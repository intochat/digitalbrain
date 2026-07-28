namespace DigitalBrain.Google;

[GenerateSerializer]
[Alias("db.google.gmail-message")]
public sealed record GmailMessage(
    [property: Id(0)] string Id,
    [property: Id(1)] string Subject,
    [property: Id(2)] string Sender,
    [property: Id(3)] string PlaintextBody);
