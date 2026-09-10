namespace DigitalBrain.Google;

[GenerateSerializer, Alias("db.gmail.refreshed")]
public sealed record GmailRefreshed([property: Id(0)] GmailConnection Connection);
