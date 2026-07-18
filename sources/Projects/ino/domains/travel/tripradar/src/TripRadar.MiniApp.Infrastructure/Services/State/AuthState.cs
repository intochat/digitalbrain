using Microsoft.JSInterop;
using System.Net.Http.Json;
using System.Text.Json;
using TripRadar.MiniApp.Client.Infrastructure.Contracts;
using TripRadar.MiniApp.Client.Infrastructure.Models.Common;

namespace TripRadar.MiniApp.Client.Infrastructure.Services.State;

public sealed class AuthState(IJSRuntime js, HttpClient http) : IAuthTokenProvider
{
    private string? _token;
    private string? _refreshToken;
    private bool _initialized;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public string? Token => _token;
    public bool IsAuthenticated => !string.IsNullOrEmpty(_token);
    public string? LastError { get; private set; }
    public void ClearError() => LastError = null;

    public event Action? OnChanged;

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _token = await js.InvokeAsync<string?>("sessionStore.get", "auth_token");
        _refreshToken = await js.InvokeAsync<string?>("sessionStore.get", "refresh_token");
        _initialized = true;
        OnChanged?.Invoke();
    }

    public async Task<bool> LoginWithTelegramAsync()
    {
        LastError = null;

        string initData;
        try
        {
            initData = await js.InvokeAsync<string>("getTelegramInitData");
        }
        catch (Exception ex)
        {
            LastError = $"JS interop error: {ex.Message}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(initData))
        {
            LastError = "Telegram initData is empty";
            return false;
        }

        return await ExchangeTelegramSessionAsync(initData);
    }

    private async Task<bool> ExchangeTelegramSessionAsync(string initData)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tokens/sessions/telegram");
            request.Headers.TryAddWithoutValidation("X-Client-Type", "api");
            request.Content = JsonContent.Create(new TelegramSessionRequest(initData));

            var response = await http.SendAsync(request, cts.Token);
            var raw = await response.Content.ReadAsStringAsync(cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                LastError = $"Telegram auth {(int)response.StatusCode}: {raw[..Math.Min(raw.Length, 300)]}";
                return false;
            }

            var result = JsonSerializer.Deserialize<LoginResponse>(raw, JsonOpts);
            if (result is null || string.IsNullOrEmpty(result.Token))
            {
                LastError = "Telegram auth failed";
                return false;
            }

            await SetTokensAsync(result.Token, result.RefreshToken);
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"Telegram auth failed: {ex.Message}";
            return false;
        }
    }

    public async Task<bool> LoginDevAsync(long telegramUserId = 123456, string? tier = null)
    {
        LastError = null;
        var success = await ExchangeTokenAsync("/api/v1/tokens/dev", new { telegramUserId, tier });

        if (success)
            await TryRegisterDevChatIdAsync($"tg_{telegramUserId}", telegramUserId);

        return success;
    }

    public async Task<long?> ResolveTelegramHandleAsync(string handle)
    {
        LastError = null;
        var normalized = handle.TrimStart('@');

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await http.GetAsync($"/api/dev/resolve/{Uri.EscapeDataString(normalized)}", cts.Token);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                LastError = $"@{normalized} has not /start-ed the bot. Open the bot in Telegram and send /start, then try again.";
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                LastError = $"Resolver returned {(int)response.StatusCode}";
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<ResolveResponse>(JsonOpts, cts.Token);
            return result?.TelegramUserId;
        }
        catch (Exception ex)
        {
            LastError = $"Resolver failed: {ex.Message}";
            return null;
        }
    }

    private async Task TryRegisterDevChatIdAsync(string username, long fallbackChatId)
    {
        long chatId = 0;
        try
        {
            chatId = await js.InvokeAsync<long?>("getTelegramChatId") ?? 0;
        }
        catch
        {
            // JS interop unavailable (running outside Telegram WebApp).
        }

        // Browser dev: chat_id equals telegram user ID for private bot DMs.
        if (chatId <= 0)
            chatId = fallbackChatId;

        if (chatId <= 0)
            return;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await http.PostAsJsonAsync("/api/dev/register-tracking-user",
                new { username, chatId }, cts.Token);
        }
        catch
        {
            // non-critical — tracking registration failures shouldn't block login
        }
    }

    private sealed record ResolveResponse(long TelegramUserId, long ChatId);

    public async Task SetTokenAsync(string token)
    {
        await SetTokensAsync(token, null);
    }

    private async Task SetTokensAsync(string token, string? refreshToken)
    {
        _token = token;
        _refreshToken = refreshToken;
        LastError = null;
        await js.InvokeVoidAsync("sessionStore.set", "auth_token", token);
        if (refreshToken is not null)
            await js.InvokeVoidAsync("sessionStore.set", "refresh_token", refreshToken);
        else
            await js.InvokeVoidAsync("sessionStore.remove", "refresh_token");
        OnChanged?.Invoke();
    }

    public async Task ClearAsync()
    {
        _token = null;
        _refreshToken = null;
        _initialized = false;
        await js.InvokeVoidAsync("sessionStore.remove", "auth_token");
        await js.InvokeVoidAsync("sessionStore.remove", "refresh_token");
        await js.InvokeVoidAsync("sessionStore.remove", "culture_synced");
        OnChanged?.Invoke();
    }

    public async Task<bool> TryRefreshAsync()
    {
        if (string.IsNullOrEmpty(_refreshToken))
        {
            LastError = "No refresh token available";
            return false;
        }

        return await ExchangeTokenAsync("/api/v1/tokens/refresh-tokens",
            new RefreshTokenRequest(_refreshToken, _token));
    }

    private async Task<bool> ExchangeTokenAsync(string endpoint, object body)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.TryAddWithoutValidation("X-Client-Type", "api");
            request.Content = JsonContent.Create(body);

            var response = await http.SendAsync(request, cts.Token);
            var raw = await response.Content.ReadAsStringAsync(cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                LastError = $"Auth API {(int)response.StatusCode}: {raw[..Math.Min(raw.Length, 300)]}";
                return false;
            }

            var login = JsonSerializer.Deserialize<LoginResponse>(raw, JsonOpts);
            if (string.IsNullOrEmpty(login?.Token))
            {
                LastError = $"Empty token in response";
                return false;
            }

            await SetTokensAsync(login.Token, login.RefreshToken);
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"Auth request failed: {ex.Message}";
            return false;
        }
    }
}