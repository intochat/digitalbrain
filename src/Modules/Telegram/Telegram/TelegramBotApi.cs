using System.Text.Json;
using System.Text.RegularExpressions;
using OpenTelemetry;
using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableTypes;
using Telegram.BotAPI.GettingUpdates;

namespace DigitalBrain.Telegram;

/// <summary>Direct provider transport: token-bearing URLs never enter factory logs or OTel traces.</summary>
public sealed partial class TelegramBotApi(HttpClient client) : IDisposable
{
    [GeneratedRegex(@"\A[0-9]+:[A-Za-z0-9_-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
    public static bool IsTokenValid(string token) => token.Length <= 256 && TokenPattern().IsMatch(token);

    public Task<User> GetMeAsync(string token, CancellationToken cancellationToken) =>
        InvokeAsync(token, "getMe", bot => bot.GetMeAsync(cancellationToken));

    public Task<bool> SetWebhookAsync(string token, string url, string secret, CancellationToken cancellationToken) =>
        InvokeAsync(token, "setWebhook", bot => bot.SetWebhookAsync(new SetWebhookArgs(url)
        {
            SecretToken = secret, AllowedUpdates = ["message"], DropPendingUpdates = false
        }, cancellationToken));

    public Task<bool> SetMenuButtonAsync(string token, string miniAppUrl, CancellationToken cancellationToken) =>
        InvokeAsync(token, "setChatMenuButton", bot => bot.SetChatMenuButtonAsync(
            menuButton: new MenuButtonWebApp { Text = "Open projects", WebApp = new WebAppInfo(miniAppUrl) },
            cancellationToken: cancellationToken));

    public Task<WebhookInfo> GetWebhookInfoAsync(string token, CancellationToken cancellationToken) =>
        InvokeAsync(token, "getWebhookInfo", bot => bot.GetWebhookInfoAsync(cancellationToken));

    private async Task<T> InvokeAsync<T>(string token, string operation, Func<ITelegramBotClient, Task<T>> call)
    {
        if (!IsTokenValid(token))
        {
            throw new InvalidOperationException("Enter the bot token provided by BotFather in the Telegram secret parameter.");
        }
        using var suppression = SuppressInstrumentationScope.Begin();
        try
        {
            return await call(new TelegramBotClient(new TelegramBotClientOptions(token, client))).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation exceptions can contain token-bearing request details too.
            throw new OperationCanceledException("Telegram request was cancelled.");
        }
        catch (HttpRequestException error)
        {
            var detail = error.StatusCode is { } code ? $" (HTTP {(int)code})" : "";
            throw new InvalidOperationException($"Telegram {operation} could not reach the provider{detail}. Check network access.");
        }
        catch (BotRequestException error)
        {
            throw new InvalidOperationException($"Telegram rejected {operation} (code {error.ErrorCode}). Check the bot configuration.");
        }
        catch (Exception)
        {
            // SDK exceptions can carry raw provider bodies or credential-bearing URLs.
            throw new InvalidOperationException($"Telegram {operation} returned an invalid response.");
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
