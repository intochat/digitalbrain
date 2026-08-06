namespace DigitalBrain;

internal static class DeliveryPolicy
{
    internal const int MaximumAttempts = 1000;
    internal static readonly TimeSpan RetryHorizon = TimeSpan.FromMinutes(30);
    internal static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(50);
    internal static readonly TimeSpan WakeupCadence = TimeSpan.FromMinutes(1);
}
