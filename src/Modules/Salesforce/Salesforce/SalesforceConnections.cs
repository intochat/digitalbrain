using System.Collections.Concurrent;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Sdk;

namespace DigitalBrain.Salesforce;

// The kernel owns one connection per owner. Tokens are intentionally volatile: restarting the
// kernel disconnects Salesforce, without persisting credentials in Orleans.
internal sealed class SalesforceConnections(SalesforceOAuthConfiguration configuration) : IMcpCredentials<OwnerId>, IDisposable
{
    private readonly ConcurrentDictionary<OwnerId, CredentialSlot> _credentials = new();
    private readonly HttpClient _oauthHttp = new(new HttpClientHandler { AllowAutoRedirect = false });

    internal OwnerId CurrentOwner => AgentTurnContext.Current?.Chat.Owner ?? configuration.Owner;

    public OwnerId Connection(OwnerId owner) => owner;

    internal async Task StoreAsync(
        OwnerId owner, string? accessToken, string? refreshToken, TimeSpan? expiresIn,
        Action<Action> commitIfActive, CancellationToken cancellationToken)
    {
        ValidateToken(accessToken);
        var slot = _credentials.GetOrAdd(owner, static _ => new CredentialSlot());
        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            commitIfActive(() =>
            {
                slot.AccessToken = accessToken;
                slot.RefreshToken = refreshToken;
                slot.ExpiresAt = Expiry(expiresIn);
            });
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    public async Task<string> AccessTokenAsync(OwnerId owner, OwnerId connection, bool refresh, CancellationToken cancellationToken)
    {
        var slot = _credentials.GetOrAdd(owner, static _ => new CredentialSlot());
        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!refresh && slot.AccessToken is not null && slot.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
            {
                return slot.AccessToken;
            }
            if (slot.RefreshToken is null)
            {
                throw new McpAuthenticationRequiredException();
            }
            using var request = configuration.RefreshRequest(slot.RefreshToken);
            using var response = await _oauthHttp.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    throw new HttpRequestException("Salesforce token refresh is temporarily unavailable. Try again shortly.", null, response.StatusCode);
                }
                slot.AccessToken = null;
                slot.RefreshToken = null;
                throw new McpAuthenticationRequiredException();
            }
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            var token = root.GetProperty("access_token").GetString();
            ValidateToken(token);
            slot.AccessToken = token;
            if (root.TryGetProperty("refresh_token", out var refreshed))
            {
                slot.RefreshToken = refreshed.GetString();
            }
            slot.ExpiresAt = root.TryGetProperty("expires_in", out var expires)
                && double.TryParse(expires.ToString(), System.Globalization.CultureInfo.InvariantCulture, out var seconds)
                    ? Expiry(TimeSpan.FromSeconds(seconds)) : DateTimeOffset.MaxValue;
            return token!;
        }
        catch (JsonException)
        {
            slot.AccessToken = null;
            slot.RefreshToken = null;
            throw new McpAuthenticationRequiredException();
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    // The access token was refused twice; the refresh token stays so the next call can try again.
    public async Task RejectAsync(OwnerId owner, OwnerId connection, CancellationToken cancellationToken)
    {
        if (!_credentials.TryGetValue(owner, out var slot))
        {
            return;
        }
        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            slot.AccessToken = null;
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    private static DateTimeOffset Expiry(TimeSpan? lifetime)
        => lifetime is { } duration ? DateTimeOffset.UtcNow.Add(duration) : DateTimeOffset.MaxValue;

    private static void ValidateToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            throw new InvalidOperationException("Salesforce did not issue a valid bearer token.");
        }
    }

    public void Dispose()
    {
        _oauthHttp.Dispose();
        foreach (var slot in _credentials.Values)
        {
            slot.Gate.Dispose();
        }
        _credentials.Clear();
    }

    private sealed class CredentialSlot
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal string? AccessToken;
        internal string? RefreshToken;
        internal DateTimeOffset ExpiresAt;
    }
}
