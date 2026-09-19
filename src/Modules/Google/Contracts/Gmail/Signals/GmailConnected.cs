using DigitalBrain.Contracts;

namespace DigitalBrain.Google;

[GenerateSerializer, Alias("gmail.connected")]
public sealed record GmailConnected([property: Id(0)] string EmailAddress) : Signal;
