namespace DigitalBrain.Google;

internal sealed class GmailTokenRefresh(IGmailTokenExchange exchange)
{
    internal async Task<GmailState> RefreshAsync(GmailState connection, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (connection.RefreshToken is null)
        {
            throw new GmailNotConnectedException();
        }
        var grant = await exchange.ExchangeAsync(connection.RefreshToken, cancellationToken).ConfigureAwait(false);
        var scopes = grant.GrantedScopes ?? connection.GrantedScopes;
        var grants = ParseScopes(scopes);
        if (!grants.Contains(GmailOAuthConfiguration.ReadScope)
            || connection.CanCompose && !grants.Contains(GmailOAuthConfiguration.ComposeScope))
        {
            throw new GmailNotConnectedException();
        }
        ValidateToken(grant.AccessToken);
        if (grant.RefreshToken is not null)
        {
            ValidateToken(grant.RefreshToken);
        }
        return connection with
        {
            AccessToken = grant.AccessToken,
            RefreshToken = grant.RefreshToken ?? connection.RefreshToken,
            GrantedScopes = scopes,
            ExpiresAt = Expiry(grant.ExpiresInSeconds, clock),
        };
    }

    internal static HashSet<string> ParseScopes(string scopes)
        => scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

    internal static DateTimeOffset Expiry(int seconds, TimeProvider clock)
        => seconds is > 0 and <= 86400 ? clock.GetUtcNow().AddSeconds(seconds)
            : throw new GmailUnavailableException("Google returned an invalid token lifetime.");

    internal static void ValidateToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 16384 || token.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
        {
            throw new GmailUnavailableException("Google returned an invalid token.");
        }
    }

}
