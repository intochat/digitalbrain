namespace DigitalBrain.Salesforce;

internal sealed class SalesforceTokenRefresh(ISalesforceTokenExchange exchange)
{
    internal async Task<SalesforceTokenGrant> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        ValidateToken(refreshToken);
        var grant = await exchange.ExchangeAsync(refreshToken, cancellationToken).ConfigureAwait(false);
        ValidateToken(grant.AccessToken);
        if (grant.RefreshToken is not null)
        {
            ValidateToken(grant.RefreshToken);
        }

        return grant;
    }

    internal static DateTimeOffset Expiry(int seconds, TimeProvider clock)
        => seconds is > 0 and <= 86400 ? clock.GetUtcNow().AddSeconds(seconds)
            : throw new SalesforceUnavailableException("Salesforce returned an invalid token lifetime.");

    internal static void ValidateToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 16384 || token.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
        {
            throw new SalesforceUnavailableException("Salesforce did not issue a valid bearer token.");
        }
    }

}
