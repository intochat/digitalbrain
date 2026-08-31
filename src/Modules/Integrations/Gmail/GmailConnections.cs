using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Integrations.Gmail;

// No serializers, records or public token properties: this volatile kernel-private store
// is deliberately lost on restart. One selected validated Google account per owner.
internal sealed class GmailConnections(GmailOAuthConfiguration configuration) : IDisposable
{
    private readonly ConcurrentDictionary<OwnerId, Slot> _owners = new();
    private readonly HttpClient _oauth = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 65536 };

    internal GmailIdentity Identity(OwnerId owner)
    {
        if (_owners.TryGetValue(owner, out var slot) && slot.Identity is { } identity)
        {
            return identity;
        }

        throw new GmailAuthenticationRequiredException();
    }

    internal async Task AcceptAsync(OwnerId owner, string sub, string email, string accessToken,
        string? refreshToken, string scopes, string? expiresIn, bool compose, Action<Action> commitIfActive,
        CancellationToken cancellationToken)
    {
        ValidateToken(accessToken);
        if (refreshToken is not null)
        {
            ValidateToken(refreshToken);
        }

        var grants = ParseScopes(scopes);
        if (!grants.Contains(GmailOAuthConfiguration.ReadScope) || !grants.Contains("openid")
            || !(grants.Contains("email") || grants.Contains("https://www.googleapis.com/auth/userinfo.email"))
            || compose && !grants.Contains(GmailOAuthConfiguration.ComposeScope))
        {
            throw new GmailOperationException("Google did not grant all required Gmail and identity scopes.");
        }

        Slot slot;
        lock (_owners)
        {
            if (_owners.Count >= 128 && !_owners.ContainsKey(owner))
            {
                throw new GmailOperationException("Too many Gmail connections. Restart the kernel to clear unused connections.");
            }
            slot = _owners.GetOrAdd(owner, static _ => new Slot());
        }
        var expiry = Expiry(expiresIn);
        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Publication and pending-action cancellation share the same lock. Nothing can
            // publish credentials after cancellation wins the transaction.
            commitIfActive(() =>
            {
                var old = slot.Identity;
                slot.RefreshToken = refreshToken ?? (old?.Subject == sub ? slot.RefreshToken : null);
                slot.AccessToken = accessToken;
                slot.ExpiresAt = expiry;
                slot.Identity = new GmailIdentity(sub, email, Guid.NewGuid(), grants.Contains(GmailOAuthConfiguration.ComposeScope));
            });
        }
        finally { slot.Gate.Release(); }
    }

    internal async Task<string> AccessTokenAsync(OwnerId owner, GmailIdentity expected, bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!_owners.TryGetValue(owner, out var slot))
        {
            throw new GmailAuthenticationRequiredException();
        }

        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (slot.Identity != expected)
            {
                throw new GmailAuthenticationRequiredException();
            }

            if (!forceRefresh && slot.AccessToken is { } current && slot.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
            {
                return current;
            }

            if (slot.RefreshToken is null) { Clear(slot); throw new GmailAuthenticationRequiredException(); }
            configuration.RequireConfigured();
            using var response = await _oauth.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = configuration.ClientId,
                ["client_secret"] = configuration.ClientSecret,
                ["refresh_token"] = slot.RefreshToken,
                ["grant_type"] = "refresh_token",
            }), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                {
                    using var failure = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (failure.RootElement.TryGetProperty("error", out var error) && error.GetString() == "invalid_grant")
                    { Clear(slot); throw new GmailAuthenticationRequiredException(); }
                }
                throw new GmailOperationException($"Gmail token refresh failed (HTTP {(int)response.StatusCode}). Check OAuth configuration or try again later.");
            }
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var root = json.RootElement;
            if (!root.TryGetProperty("token_type", out var tokenType)
                || !string.Equals(tokenType.GetString(), "Bearer", StringComparison.OrdinalIgnoreCase))
            {
                throw new GmailOperationException("Google returned an unsupported token type.");
            }
            if (root.TryGetProperty("scope", out var scope))
            {
                var grants = ParseScopes(scope.GetString() ?? "");
                if (!grants.Contains(GmailOAuthConfiguration.ReadScope)
                    || expected.CanCompose && !grants.Contains(GmailOAuthConfiguration.ComposeScope))
                { Clear(slot); throw new GmailAuthenticationRequiredException(); }
            }
            var token = root.GetProperty("access_token").GetString();
            ValidateToken(token);
            slot.AccessToken = token;
            slot.ExpiresAt = Expiry(root.GetProperty("expires_in").ToString());
            return token!;
        }
        catch (GmailAuthenticationRequiredException) { throw; }
        catch (GmailOperationException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { throw new GmailOperationException("Gmail token refresh is unavailable. Try again later; no new consent request was started."); }
        finally { slot.Gate.Release(); }
    }

    internal async Task RejectAsync(OwnerId owner, GmailIdentity expected, CancellationToken cancellationToken)
    {
        if (!_owners.TryGetValue(owner, out var slot))
        {
            return;
        }

        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (slot.Identity == expected)
            {
                Clear(slot);
            }
        }
        finally { slot.Gate.Release(); }
    }

    private static void Clear(Slot slot) { slot.AccessToken = null; slot.RefreshToken = null; slot.Identity = null; }
    private static HashSet<string> ParseScopes(string scopes) => scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    private static DateTimeOffset Expiry(string? seconds) => double.TryParse(seconds, CultureInfo.InvariantCulture, out var value)
        && value > 0 && value <= 86400 ? DateTimeOffset.UtcNow.AddSeconds(value)
        : throw new GmailOperationException("Google returned an invalid token lifetime.");
    private static void ValidateToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 16384 || token.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
        {
            throw new GmailOperationException("Google returned an invalid token.");
        }
    }
    public void Dispose() { _oauth.Dispose(); _owners.Clear(); }
    private sealed class Slot
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal volatile GmailIdentity? Identity;
        internal string? AccessToken;
        internal string? RefreshToken;
        internal DateTimeOffset ExpiresAt;
    }
}

internal sealed record GmailIdentity(string Subject, string Email, Guid Revision, bool CanCompose);
