using System.Text.Json.Serialization;

namespace DigitalBrain;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$journalRead")]
[JsonDerivedType(typeof(JournalPage), "page")]
[JsonDerivedType(typeof(JournalHistoryUnavailable), "historyUnavailable")]
public abstract record JournalRead;
