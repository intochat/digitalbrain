using DigitalBrain.Abstractions;

namespace DigitalBrain.Google;

[Alias("db.google.gmail")]
public interface IGmail : INeuron
{
    [Alias("ReadMessage")]
    Task<GmailMessage> ReadMessageAsync(
        string messageId,
        CancellationToken cancellationToken);
}
