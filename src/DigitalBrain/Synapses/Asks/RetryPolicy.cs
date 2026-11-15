namespace DigitalBrain.Units;

/// <summary>Determines retry backoff delays for transient failures of repeatable asks.</summary>
internal static class RetryPolicy
{
    private static readonly TimeSpan[] Delays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10)];

    /// <summary>Returns the backoff delay before retrying after failed <paramref name="attempt"/>.</summary>
    internal static TimeSpan Next(int attempt) =>
        Delays[Math.Clamp(attempt - 1, 0, Delays.Length - 1)];
}
