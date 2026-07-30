using DigitalBrain.Abstractions;

namespace DigitalBrain.TestingTests;

internal static class TestingScenario
{
    public const string WelcomeGreeter = "welcome";
    public const string Session = ISessionNeuron.InstanceName;
    public const string OtherOwner = "other";
    public const string CapabilityCaller = "caller";
    public const string CapabilityTarget = "target";
    public const string Echo = "echo";
    public const string ReplyProbe = "reply-probe";
    public const string Guest = "Ada";

    public static string GreetedMessage(string guest)
        => $"Hello, {guest}.";
}
