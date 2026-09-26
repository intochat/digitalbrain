using DigitalBrain.Contracts;

namespace DigitalBrain.Google.Gmail;

[GenerateSerializer, Alias("gmail.message-arrived")]
public sealed record GmailMessageArrived(
    [property: Id(0)] string EmailAddress,
    [property: Id(1)] string MessageId,
    [property: Id(2)] string Subject) : Signal;