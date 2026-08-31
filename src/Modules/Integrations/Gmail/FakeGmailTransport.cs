namespace DigitalBrain.Integrations.Gmail;

public sealed class FakeGmailTransport : IGmailTransport
{
    public Task<string> SearchJsonAsync(DigitalBrain.Abstractions.Identity.OwnerId owner, string account, string topic, CancellationToken cancellationToken)
        => SearchJsonAsync(account, topic, cancellationToken);
    public Task<string> SearchJsonAsync(string account, string topic, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            """{"threads":[{"id":"thread-intochat","messages":[{"id":"message-intochat","subject":"New Customer","snippet":"Please send company information.","sender":"vlad@intochat.io"}]}]}""");
    }
}
