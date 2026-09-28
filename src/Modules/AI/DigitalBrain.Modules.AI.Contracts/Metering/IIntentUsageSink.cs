using DigitalBrain.Contracts;

namespace DigitalBrain.AI.Metering;

/// <summary>
/// The narrow seam between the metering decorators and durable storage. The decorator owns
/// reading the provider's usage; the sink owns accumulating it in the ambient intent scope and
/// flushing the whole intent's batch to storage exactly once when the intent completes.
/// </summary>
public interface IIntentUsageSink
{
    Task RecordAsync(string intentId, TokenUsageEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists every entry the <paramref name="intent"/> accumulated in one durable write. A batch
    /// is only written when the intent recorded at least one provider call.
    /// </summary>
    Task FlushAsync(IntentContext intent, CancellationToken cancellationToken = default);
}