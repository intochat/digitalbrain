namespace DigitalBrain.Salesforce;

internal sealed class FakeSalesforceTokenExchange : ISalesforceTokenExchange
{
    public Task<SalesforceTokenGrant> ExchangeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SalesforceTokenRefresh.ValidateToken(refreshToken);
        return Task.FromResult(new SalesforceTokenGrant("fake-refreshed-" + Guid.NewGuid().ToString("N"), null, 3600));
    }
}
