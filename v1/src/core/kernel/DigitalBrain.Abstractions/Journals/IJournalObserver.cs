using Orleans.Concurrency;

namespace DigitalBrain.Abstractions;

[Alias("db.journal-observer")]
public interface IJournalObserver : IGrainObserver
{
    // OneWay: journal is committed before notify; awaiting observers reenters/hangs the neuron turn.
    [OneWay]
    [Alias(nameof(ObserveAsync))]
    Task ObserveAsync(JournalKind kind, JournalRead read);
}
