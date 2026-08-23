namespace DigitalBrain.Integrations.Gmail;

public sealed class FakeGmailTransport : IGmailTransport
{
    public Task<string> SearchJsonAsync(string account, string topic, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            """{"messages":[{"id":"1","subject":"New Customer","from":"lead@acme.test"}]}""");
    }
}
