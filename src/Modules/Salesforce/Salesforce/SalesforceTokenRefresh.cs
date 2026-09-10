using System.Globalization;
using System.Text.Json;

namespace DigitalBrain.Salesforce;

internal sealed class SalesforceTokenRefresh(SalesforceOAuthConfiguration configuration) : IDisposable
{
    private readonly HttpClient _oauth = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 65536 };

    internal async Task<SalesforceState> RefreshAsync(SalesforceState connection, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (connection.RefreshToken is null)
        {
            throw new SalesforceNotConnectedException();
        }
        using var request = configuration.RefreshRequest(connection.RefreshToken);
        using var response = await _oauth.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                throw new SalesforceUnavailableException("Salesforce token refresh is temporarily unavailable. Try again shortly.");
            }
            throw new SalesforceNotConnectedException();
        }
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var root = document.RootElement;
            var token = root.GetProperty("access_token").GetString();
            ValidateToken(token);
            var refreshToken = root.TryGetProperty("refresh_token", out var refreshed) ? refreshed.GetString() : connection.RefreshToken;
            if (refreshToken is not null)
            {
                ValidateToken(refreshToken);
            }
            return connection with
            {
                AccessToken = token,
                RefreshToken = refreshToken,
                ExpiresAt = root.TryGetProperty("expires_in", out var expires)
                    && double.TryParse(expires.ToString(), CultureInfo.InvariantCulture, out var seconds)
                        ? clock.GetUtcNow().AddSeconds(seconds) : DateTimeOffset.MaxValue,
            };
        }
        catch (JsonException)
        {
            throw new SalesforceNotConnectedException();
        }
    }

    internal static DateTimeOffset Expiry(int seconds, TimeProvider clock)
        => seconds is > 0 and <= 86400 ? clock.GetUtcNow().AddSeconds(seconds)
            : throw new SalesforceUnavailableException("Salesforce returned an invalid token lifetime.");

    internal static void ValidateToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 16384 || token.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
        {
            throw new SalesforceUnavailableException("Salesforce did not issue a valid bearer token.");
        }
    }

    public void Dispose() => _oauth.Dispose();
}
