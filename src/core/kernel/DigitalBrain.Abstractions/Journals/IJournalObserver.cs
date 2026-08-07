using Orleans.Concurrency;

namespace DigitalBrain.Abstractions;

[Alias("db.journal-observer")]
public interface IJournalObserver : IGrainObserver
{
    [OneWay]
    [Alias(nameof(ObserveAsync))]
    Task ObserveAsync(JournalKind kind, JournalRead read);
}
