using System.Net;
using System.Text.Json;

namespace DigitalBrain.Google;

internal sealed class GmailTokenRefresh(GmailOAuthConfiguration configuration) : IDisposable
{
    private readonly HttpClient _oauth = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 65536 };

    internal async Task<GmailState> RefreshAsync(GmailState connection, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (connection.RefreshToken is null)
        {
            throw new GmailNotConnectedException();
        }
        configuration.RequireConfigured();
        try
        {
            using var response = await _oauth.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = configuration.ClientId,
                ["client_secret"] = configuration.ClientSecret,
                ["refresh_token"] = connection.RefreshToken,
                ["grant_type"] = "refresh_token",
            }), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                {
                    using var failure = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (failure.RootElement.TryGetProperty("error", out var error) && error.GetString() == "invalid_grant")
                    {
                        throw new GmailNotConnectedException();
                    }
                }
                throw new GmailUnavailableException($"Gmail token refresh failed (HTTP {(int)response.StatusCode}). Check OAuth configuration or try again later.");
            }
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var root = json.RootElement;
            if (!root.TryGetProperty("token_type", out var tokenType)
                || !string.Equals(tokenType.GetString(), "Bearer", StringComparison.OrdinalIgnoreCase))
            {
                throw new GmailUnavailableException("Google returned an unsupported token type.");
            }
            if (root.TryGetProperty("scope", out var scope))
            {
                var grants = ParseScopes(scope.GetString() ?? "");
                if (!grants.Contains(GmailOAuthConfiguration.ReadScope)
                    || connection.CanCompose && !grants.Contains(GmailOAuthConfiguration.ComposeScope))
                {
                    throw new GmailNotConnectedException();
                }
            }
            var token = root.GetProperty("access_token").GetString();
            ValidateToken(token);
            return connection with
            {
                AccessToken = token,
                ExpiresAt = Expiry(root.GetProperty("expires_in").GetInt32(), clock),
            };
        }
        catch (GmailNotConnectedException) { throw; }
        catch (GmailUnavailableException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            throw new GmailUnavailableException("Gmail token refresh is unavailable. Try again later; no new consent request was started.");
        }
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

    public void Dispose() => _oauth.Dispose();
}
