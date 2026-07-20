namespace DigitalBrain.Abstractions;

[Alias("db.journal-observer")]
public interface IJournalObserver : IGrainObserver
{
    [Alias("Observe")]
    Task ObserveAsync(JournalKind kind, JournalRead read);
}
