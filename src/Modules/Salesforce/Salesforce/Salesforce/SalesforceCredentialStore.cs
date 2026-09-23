using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Contracts.Types;
using DigitalBrain.MyData;

namespace DigitalBrain.Salesforce;

// The Salesforce tokens live only in the owner's My Data vault. This type writes them on connect
// and refresh, and resolves a value only inside the neuron that is about to make the outbound call.
internal sealed class SalesforceCredentialStore(
    ISecretResolver secrets,
    IGrainFactory grains,
    SalesforceTokenRefresh refresh,
    TimeProvider time)
{
    internal const string AccessFieldPath = "salesforce.access";
    internal const string RefreshFieldPath = "salesforce.refresh";

    internal async Task<SalesforceState> StoreAsync(
        string owner, SalesforceState connection, string accessToken, string? refreshToken, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        var vault = grains.GetGrain<IVault>(owner);
        var platform = Platform();
        connection = connection with
        {
            AccessToken = await vault.SetSecret(platform, AccessFieldPath, "Salesforce access token", accessToken, cancellationToken),
        };
        if (refreshToken is not null)
        {
            connection = connection with
            {
                RefreshToken = await vault.SetSecret(platform, RefreshFieldPath, "Salesforce refresh token", refreshToken, cancellationToken),
            };
        }

        return connection with { ExpiresAt = expiresAt };
    }

    internal async Task<SalesforceState> RefreshAsync(SalesforceState connection, CancellationToken cancellationToken)
    {
        var refreshToken = await RefreshTokenAsync(connection, cancellationToken).ConfigureAwait(false);
        var grant = await refresh.RefreshAsync(refreshToken, cancellationToken).ConfigureAwait(false);
        var expiresAt = Expiry(grant.ExpiresInSeconds);
        return await StoreAsync(OwnerOf(connection.AccessToken!), connection, grant.AccessToken, grant.RefreshToken, expiresAt, cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task<string> AccessTokenAsync(SalesforceState connection, CancellationToken cancellationToken)
        => await secrets.ResolveAsync(
            connection.AccessToken ?? throw new SalesforceNotConnectedException(),
            Platform(),
            cancellationToken).ConfigureAwait(false);

    internal async Task<string> RefreshTokenAsync(SalesforceState connection, CancellationToken cancellationToken)
        => connection.RefreshToken is null
            ? throw new SalesforceNotConnectedException()
            : await secrets.ResolveAsync(connection.RefreshToken, Platform(), cancellationToken).ConfigureAwait(false);

    private DateTimeOffset Expiry(double? seconds)
    {
        try
        {
            return seconds is { } value ? time.GetUtcNow().AddSeconds(value) : DateTimeOffset.MaxValue;
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new SalesforceUnavailableException("Salesforce returned an invalid token lifetime.");
        }
    }

    private static string OwnerOf(SecretRef secret)
    {
        var parts = secret.Reference.Split('/');
        return parts.Length >= 4 ? parts[2] : "";
    }

    // The outbound call resolves the secret as trusted platform code, never as the user turn.
    private static CallerContext Platform() => new()
    {
        PrincipalId = "salesforce",
        AccountId = "salesforce",
        WorkspaceId = "salesforce",
        Kind = CallerKind.Platform,
        StampedBy = TrustedEdge.Platform,
        AppId = "salesforce",
    };
}
