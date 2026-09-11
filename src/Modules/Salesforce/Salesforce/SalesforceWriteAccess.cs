namespace DigitalBrain.Salesforce;

internal sealed class SalesforceWriteAccess(ISalesforceProvider provider, SalesforceTokenRefresh refresh)
{
    internal async Task<(SalesforceState Connection, string? SchemaHash, string? Reason)> ReadAsync(
        string tool, SalesforceState connection, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (connection.ExpiresAt <= clock.GetUtcNow().AddSeconds(30))
        {
            connection = await refresh.RefreshAsync(connection, clock, cancellationToken).ConfigureAwait(false);
        }
        try
        {
            var hash = await provider.ReadToolSchemaHashAsync(tool, connection.AccessToken!, cancellationToken).ConfigureAwait(false);
            return (connection, hash, null);
        }
        catch (SalesforceUnavailableException error)
        {
            return (connection, null, error.Message);
        }
    }
}
