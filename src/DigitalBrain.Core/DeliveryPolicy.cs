namespace DigitalBrain;

// The one home of every delivery, retry, ask and retention bound (§7) — v1 values unless
// the design says otherwise. Bounded retry is physics #4: never silent loss, never
// infinite silent retry.
internal static class DeliveryPolicy
{
    internal const int MaximumAttempts = 1000;

    internal static readonly TimeSpan RetryHorizon = TimeSpan.FromMinutes(30);

    // Finite bound per Deliver attempt so reminder-driven drains always hold a token that
    // can actually fire, not one that is merely CanBeCanceled forever.
    internal static readonly TimeSpan DeliveryAttemptTimeout = TimeSpan.FromSeconds(30);

    // The fast in-activation drain timer.
    internal static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(50);

    // The durable reminder backstop for neurons that go idle with an unsettled outbox.
    internal static readonly TimeSpan WakeupCadence = TimeSpan.FromMinutes(1);

    // Twice the retry horizon: the delivery leg alone may burn a full RetryHorizon before
    // the answerer ever hears the ask; the second half covers the answer never coming.
    internal static readonly TimeSpan AskHorizon = 2 * RetryHorizon;

    internal const int ScheduleFailureLimit = 5;

    // Safe to prune past this: no delivery attempt outlives RetryHorizon, so no duplicate
    // can arrive for a source whose watermark sat untouched longer than horizon plus slack.
    internal static readonly TimeSpan WatermarkRetention = RetryHorizon + TimeSpan.FromMinutes(5);

    // Soft compaction targets, always subordinate to the floor (cursor, ask pins).
    internal const int MaxRetainedEntries = 512;

    internal const int MaxRetainedBytes = 512 * 1024;
}
