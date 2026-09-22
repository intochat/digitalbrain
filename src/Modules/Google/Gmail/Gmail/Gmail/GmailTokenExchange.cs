using System.Net;
using System.Text.Json;

namespace DigitalBrain.Google.Gmail;

internal sealed class GmailTokenExchange(GmailOAuthConfiguration configuration) : IGmailTokenExchange, IDisposable
{
    private readonly HttpClient _oauth = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 65536 };

    public async Task<GmailTokenGrant> ExchangeAuthorizationCodeAsync(string authorizationCode, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationCode);
        configuration.RequireConfigured();
        using var response = await _oauth.PostAsync(configuration.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = configuration.ClientId,
            ["client_secret"] = configuration.ClientSecret,
            ["code"] = authorizationCode,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = new Uri(configuration.PublicOrigin, "google/gmail/oauth/callback").AbsoluteUri,
        }), cancellationToken).ConfigureAwait(false);
        return await ReadGrantAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GmailTokenGrant> ExchangeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        configuration.RequireConfigured();
        using var response = await _oauth.PostAsync(configuration.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = configuration.ClientId,
            ["client_secret"] = configuration.ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        }), cancellationToken).ConfigureAwait(false);
        return await ReadGrantAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<GmailTokenGrant> ReadGrantAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
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
                throw new GmailUnreachableException($"Gmail token exchange failed (HTTP {(int)response.StatusCode}). Check OAuth configuration or try again later.");
            }
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var root = json.RootElement;
            if (!root.TryGetProperty("token_type", out var tokenType)
                || !string.Equals(tokenType.GetString(), "Bearer", StringComparison.OrdinalIgnoreCase))
            {
                throw new GmailUnavailableException("Google returned an unsupported token type.");
            }
            var token = root.GetProperty("access_token").GetString();
            GmailTokenRefresh.ValidateToken(token);
            return new GmailTokenGrant(token!,
                root.TryGetProperty("refresh_token", out var replacement) ? replacement.GetString() : null,
                root.TryGetProperty("scope", out var scope) ? scope.GetString() : null,
                root.GetProperty("expires_in").GetInt32());
        }
        catch (GmailNotConnectedException) { throw; }
        catch (GmailUnavailableException) { throw; }
        catch (GmailUnreachableException) { throw; }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new GmailUnavailableException("Gmail returned an invalid response shape.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            throw new GmailUnreachableException("Gmail token exchange is unavailable. Try again later; no new consent request was started.");
        }
    }

    public void Dispose() => _oauth.Dispose();
}