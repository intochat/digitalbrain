namespace DigitalBrain.Testing;

internal static class TestLimits
{
    public const int Buffer = 256;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(20);
}
