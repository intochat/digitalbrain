namespace DigitalBrain.SDK.Google;

[GenerateSerializer]
public sealed record GmailSender(
    [property: Id(0)] string Name,
    [property: Id(1)] string EmailAddress,
    [property: Id(2)] DateTimeOffset ReceivedUtc,
    [property: Id(3)] string Subject);
