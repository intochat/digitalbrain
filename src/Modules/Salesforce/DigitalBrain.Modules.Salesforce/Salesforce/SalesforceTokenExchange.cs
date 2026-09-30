using System.Globalization;
using System.Text.Json;

namespace DigitalBrain.Salesforce;

internal sealed class SalesforceTokenExchange(SalesforceOAuthConfiguration configuration) : ISalesforceTokenExchange, IDisposable
{
    private readonly HttpClient _oauth = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 65536 };

    public async Task<SalesforceTokenGrant> ExchangeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            using var request = configuration.RefreshRequest(refreshToken);
            using var response = await _oauth.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    throw new SalesforceUnreachableException("Salesforce token refresh is temporarily unavailable. Try again shortly.");
                }
                throw new SalesforceNotConnectedException();
            }
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var root = document.RootElement;
            var token = root.GetProperty("access_token").GetString();
            SalesforceTokenRefresh.ValidateToken(token);
            var replacement = root.TryGetProperty("refresh_token", out var refreshed) ? refreshed.GetString() : null;
            if (replacement is not null)
            {
                SalesforceTokenRefresh.ValidateToken(replacement);
            }
            return new SalesforceTokenGrant(token!, replacement,
                root.TryGetProperty("expires_in", out var expires)
                    && double.TryParse(expires.ToString(), CultureInfo.InvariantCulture, out var seconds) ? seconds : null);
        }
        catch (SalesforceNotConnectedException) { throw; }
        catch (SalesforceUnavailableException) { throw; }
        catch (SalesforceUnreachableException) { throw; }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new SalesforceUnavailableException("Salesforce returned an invalid response shape.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            throw new SalesforceUnreachableException("Salesforce token refresh is unavailable. Try again later; no new consent request was started.");
        }
    }

    public void Dispose() => _oauth.Dispose();
}
