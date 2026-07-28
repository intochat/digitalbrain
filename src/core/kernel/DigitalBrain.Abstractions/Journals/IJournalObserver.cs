namespace DigitalBrain.Abstractions;

[Alias("db.journal-observer")]
public interface IJournalObserver : IGrainObserver
{
    [Alias(nameof(ObserveAsync))]
    Task ObserveAsync(JournalKind kind, JournalRead read);
}
