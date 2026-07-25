namespace DigitalBrain.TestingTests;

internal static class TestingScenario
{
    public const string WelcomeGreeter = "welcome";
    public const string Session = "session";
    public const string OtherOwner = "other";
    public const string CapabilityCaller = "caller";
    public const string CapabilityTarget = "target";
    public const string Guest = "Ada";

    public static string GreetedMessage(string guest)
        => $"Hello, {guest}.";
}
