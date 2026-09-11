namespace DigitalBrain.Google;

[GenerateSerializer, Alias("db.gmail.connection-rejected")]
public sealed record GmailConnectionRejected([property: Id(0)] string Reason);
