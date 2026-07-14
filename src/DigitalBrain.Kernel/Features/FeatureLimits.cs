namespace DigitalBrain.Kernel.Features;

internal static class FeatureLimits
{
    public const int InstallationsPerOwner = 100;
    public const int InboxEntries = 1_000;
    public const int StateUtf8Bytes = 64 * 1024;
    public const int IntentsPerRun = 32;
    public const int ReadsPerRun = 20;
    public const int ModelCallsPerRun = 4;
    public const int AttemptsPerInput = 5;
    public const int FanOutBatches = 1_000;
    public const int IntentLedgerEntries = 256;
    public const int IntentLedgerUtf8Bytes = 4 * 1024 * 1024;
    public static readonly TimeSpan RunDeadline = TimeSpan.FromSeconds(60);
}

internal sealed class FeatureLimitExceededException(string message) : InvalidOperationException(message);

internal sealed class FeatureConcurrencyException(string message) : InvalidOperationException(message);
