namespace DigitalBrain.Integrations.Gmail;

public interface IGmailTransport
{
    Task<string> SearchJsonAsync(string account, string topic, CancellationToken cancellationToken);
    Task<string> SearchJsonAsync(DigitalBrain.Abstractions.Identity.OwnerId owner, string account, string topic, CancellationToken cancellationToken)
        => SearchJsonAsync(account, topic, cancellationToken);
}
