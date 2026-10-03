using DigitalBrain.Sdk.Types;
using DigitalBrain.Platform.Contracts.Auth;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Contracts.Types;
using DigitalBrain.Platform.Contracts.Secrets;

namespace DigitalBrain.Salesforce;

// The Salesforce tokens live in the owner's secrets grain. This type writes them on connect
// and refresh, and resolves a value only inside the neuron that is about to make the outbound call.
internal sealed class SalesforceCredentialStore(
    IOAuthCredentials credentials,
    SalesforceTokenRefresh refresh,
    TimeProvider time)
{
    internal const string AccessFieldPath = "salesforce.access";
    internal const string RefreshFieldPath = "salesforce.refresh";

    internal async Task<SalesforceState> StoreAsync(
        string owner, SalesforceState connection, string accessToken, string? refreshToken, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        connection = connection with
        {
            AccessToken = await credentials.StoreAccessToken("salesforce", owner, accessToken, cancellationToken),
        };
        if (refreshToken is not null)
        {
            connection = connection with
            {
                RefreshToken = await credentials.StoreRefreshToken("salesforce", owner, refreshToken, cancellationToken),
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
    {
        var secret = connection.AccessToken ?? throw new SalesforceNotConnectedException();
        return await credentials.ResolveAccessToken("salesforce", secret, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<string> RefreshTokenAsync(SalesforceState connection, CancellationToken cancellationToken)
    {
        var secret = connection.RefreshToken ?? throw new SalesforceNotConnectedException();
        return await credentials.ResolveRefreshToken("salesforce", secret, cancellationToken).ConfigureAwait(false);
    }

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
        return secret.OwnerOf();
    }

}
