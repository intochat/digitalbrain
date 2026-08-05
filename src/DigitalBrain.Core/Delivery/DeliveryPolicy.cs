namespace DigitalBrain;

internal static class DeliveryPolicy
{
    internal const int MaximumAttempts = 1000;

    internal static readonly TimeSpan RetryHorizon = TimeSpan.FromMinutes(30);

    internal static readonly TimeSpan DeliveryAttemptTimeout = TimeSpan.FromSeconds(30);

    internal static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(50);

    internal static readonly TimeSpan WakeupCadence = TimeSpan.FromMinutes(1);

    internal static readonly TimeSpan AskHorizon = 2 * RetryHorizon;

    internal const int ScheduleFailureLimit = 5;

    internal static readonly TimeSpan WatermarkRetention = RetryHorizon + TimeSpan.FromMinutes(5);

    internal const int MaxRetainedEntries = 512;

    internal const int MaxRetainedBytes = 512 * 1024;
}
