using Orleans;

namespace DigitalBrain;

[GenerateSerializer]
[Alias("db.journal-kind")]
public enum JournalKind
{
    Incoming,
    Outgoing,
}
