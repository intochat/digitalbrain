namespace DigitalBrain.Google;

[GenerateSerializer, Alias("db.gmail.connected")]
public sealed record GmailConnected([property: Id(0)] GmailConnection Connection);
