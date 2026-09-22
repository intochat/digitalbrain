using DigitalBrain.Contracts;

namespace DigitalBrain.Google.Gmail;

[GenerateSerializer, Alias("gmail.mail-received")]
public sealed record MailReceived(
    [property: Id(0)] string EmailAddress,
    [property: Id(1)] string HistoryId) : Signal;