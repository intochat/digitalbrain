using DigitalBrain.Abstractions;

namespace DigitalBrain.Google;

public partial interface IGmail : INeuron
{
    [Alias(nameof(ReadMessage))]
    Task<GmailMessage> ReadMessage(
        string messageId,
        CancellationToken cancellationToken);
}
