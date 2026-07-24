using DigitalBrain.Abstractions;

namespace DigitalBrain.Google;

[Alias("db.google.gmail")]
public partial interface IGmail : INeuron
{
    [Alias(nameof(ReadMessage))]
    Task<GmailMessage> ReadMessage(
        string messageId,
        CancellationToken cancellationToken);
}
