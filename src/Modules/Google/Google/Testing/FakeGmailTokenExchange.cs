namespace DigitalBrain.Google;

internal sealed class FakeGmailTokenExchange : IGmailTokenExchange
{
    public Task<GmailTokenGrant> ExchangeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GmailTokenRefresh.ValidateToken(refreshToken);
        // An omitted scope preserves the original grant, just as in Google's refresh response.
        return Task.FromResult(new GmailTokenGrant("fake-refreshed-" + Guid.NewGuid().ToString("N"), null, null, 3600));
    }
}
