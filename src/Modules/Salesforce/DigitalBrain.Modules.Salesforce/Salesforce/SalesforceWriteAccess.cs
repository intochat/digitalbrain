namespace DigitalBrain.Salesforce;

internal sealed class SalesforceWriteAccess(ISalesforceProvider provider, SalesforceCredentialStore credentials, TimeProvider clock)
{
    internal async Task<(SalesforceState Connection, string? SchemaHash, string? Reason)> ReadAsync(
        string tool, SalesforceState connection, CancellationToken cancellationToken)
    {
        if (connection.ExpiresAt <= clock.GetUtcNow().AddSeconds(30))
        {
            connection = await credentials.RefreshAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        try
        {
            var accessToken = await credentials.AccessTokenAsync(connection, cancellationToken).ConfigureAwait(false);
            var hash = await provider.ReadToolSchemaHashAsync(tool, accessToken, cancellationToken).ConfigureAwait(false);
            return (connection, hash, null);
        }
        catch (SalesforceUnavailableException error)
        {
            return (connection, null, error.Message);
        }
    }
}
