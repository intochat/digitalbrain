namespace DigitalBrain.Salesforce;

internal interface ISalesforceTokenExchange
{
    Task<SalesforceTokenGrant> ExchangeAsync(string refreshToken, CancellationToken cancellationToken);
}
