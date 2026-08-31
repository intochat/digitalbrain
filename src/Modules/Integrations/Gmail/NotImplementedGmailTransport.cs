namespace DigitalBrain.Integrations.Gmail;

public sealed class NotImplementedGmailTransport : IGmailTransport
{
    public Task<string> SearchJsonAsync(DigitalBrain.Abstractions.Identity.OwnerId owner, string account, string topic, CancellationToken cancellationToken)
        => SearchJsonAsync(account, topic, cancellationToken);
    public Task<string> SearchJsonAsync(string account, string topic, CancellationToken cancellationToken)
        => throw new NotImplementedException("Wire MCP later");
}
