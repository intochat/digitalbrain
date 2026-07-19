namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.journal-kind")]
public enum JournalKind
{
    Incoming,
    Outgoing,
}
