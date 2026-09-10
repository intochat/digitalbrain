namespace DigitalBrain.Google;

internal sealed class GmailDraftAccess(IGmailProvider provider, GmailTokenRefresh refresh)
{
    internal async Task<(GmailState Connection, string SchemaHash)> ReadAsync(
        GmailState connection, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (connection.ExpiresAt <= clock.GetUtcNow().AddSeconds(30))
        {
            connection = await refresh.RefreshAsync(connection, clock, cancellationToken).ConfigureAwait(false);
        }
        var hash = await provider.ReadToolSchemaHashAsync("create_draft", connection.AccessToken!, cancellationToken).ConfigureAwait(false);
        return (connection, hash);
    }
}
