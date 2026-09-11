namespace DigitalBrain.Google;

internal interface IGmailTokenExchange
{
    Task<GmailTokenGrant> ExchangeAsync(string refreshToken, CancellationToken cancellationToken);
}
