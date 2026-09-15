using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenTelemetry;

namespace DigitalBrain.Telegram;

/// <summary>Direct provider transport: token-bearing URLs never enter factory logs or OTel traces.</summary>
public sealed partial class TelegramBotApi(HttpClient client) : IDisposable
{
    [GeneratedRegex(@"\A[0-9]+:[A-Za-z0-9_-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
    public static bool IsTokenValid(string token) => token.Length <= 256 && TokenPattern().IsMatch(token);

    public async Task<JsonElement> CallAsync(string token, string method, object arguments, CancellationToken cancellationToken)
    {
        if (!IsTokenValid(token))
        {
            throw new InvalidOperationException("Enter the bot token provided by BotFather in the Telegram secret parameter.");
        }
        if (method is not ("getMe" or "setWebhook" or "setChatMenuButton" or "getWebhookInfo"))
        {
            throw new ArgumentException("Unsupported bot setup method.", nameof(method));
        }
        using var suppression = SuppressInstrumentationScope.Begin();
        try
        {
            using var response = await client.PostAsJsonAsync(new Uri($"https://api.telegram.org/bot{token}/{method}"), arguments, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Telegram rejected {method} (HTTP {(int)response.StatusCode}). Check the bot token and network connection.");
            }
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (!body.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True ||
                !body.RootElement.TryGetProperty("result", out var result))
            {
                throw new InvalidOperationException($"Telegram did not accept {method}. Check the bot configuration.");
            }
            return result.Clone();
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException($"Telegram {method} could not reach the provider. Check network access.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"Telegram {method} returned an invalid response.");
        }
    }

    public async Task VerifyIngressAsync(Uri origin, string secret, string instanceId, CancellationToken cancellationToken)
    {
        using var suppression = SuppressInstrumentationScope.Begin();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(origin, "/telegram/health"));
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", secret);
        try
        {
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("The public Telegram route is not reachable yet. Check the tunnel or HTTPS endpoint.");
            }
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (!body.RootElement.TryGetProperty("instanceId", out var instance) || instance.GetString() != instanceId ||
                !body.RootElement.TryGetProperty("miniAppAvailable", out var available) || available.ValueKind != JsonValueKind.True)
            {
                throw new InvalidOperationException("The public route must reach this DigitalBrain instance with its built Telegram Mini App.");
            }
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("The public Telegram route could not be reached. Check the tunnel or HTTPS endpoint.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The public Telegram route did not return a DigitalBrain readiness response.");
        }
    }

    public void Dispose() => client.Dispose();
}
