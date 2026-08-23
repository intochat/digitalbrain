namespace DigitalBrain.Integrations.Gmail;

public interface IGmailTransport
{
    Task<string> SearchJsonAsync(string account, string topic, CancellationToken cancellationToken);
}
