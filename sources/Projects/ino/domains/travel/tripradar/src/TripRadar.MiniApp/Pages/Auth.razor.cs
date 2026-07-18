using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace TripRadar.MiniApp.Pages;

public partial class Auth
{
    [SupplyParameterFromQuery(Name = "dev_login_as")] public long? DevLoginAs { get; set; }
    [SupplyParameterFromQuery(Name = "dev_login_as_handle")] public string? DevLoginAsHandle { get; set; }
    [SupplyParameterFromQuery(Name = "tier")] public string? DevTier { get; set; }
    [SupplyParameterFromQuery(Name = "redirect")] public string? RedirectPath { get; set; }

    private bool _loading = true;
    private bool _hasInitData;
    private bool _hasTelegramWebApp;
    private bool _showDevLogin;
    private string _handleInput = "100001";
    private const string Placeholder = "@username or 100001";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        if (DevLoginAs is > 0 || !string.IsNullOrWhiteSpace(DevLoginAsHandle))
        {
            long telegramUserId = DevLoginAs ?? 0;
            if (telegramUserId <= 0)
            {
                var resolved = await AuthState.ResolveTelegramHandleAsync(DevLoginAsHandle!);
                if (resolved is > 0)
                    telegramUserId = resolved.Value;
            }

            if (telegramUserId > 0 && await AuthState.LoginDevAsync(telegramUserId, DevTier))
            {
                TopBar.Reset();
                await TopBar.LoadAsync();
                Nav.NavigateTo(ResolveRedirect(), replace: true);
                return;
            }

            _showDevLogin = true;
            _loading = false;
            if (!string.IsNullOrWhiteSpace(DevLoginAsHandle))
                _handleInput = DevLoginAsHandle!;
            StateHasChanged();
            return;
        }

        try
        {
            _hasTelegramWebApp = await JS.InvokeAsync<bool>("eval", "!!(window.Telegram && window.Telegram.WebApp)");
            var initData = await JS.InvokeAsync<string>("getTelegramInitData");
            _hasInitData = !string.IsNullOrWhiteSpace(initData);
        }
        catch
        {
            // ignored
        }

        if (AuthState.IsAuthenticated)
        {
            Nav.NavigateTo(AppRoutes.Home, replace: true);
            return;
        }

        if (_hasInitData)
        {
            var success = await AuthState.LoginWithTelegramAsync();
            if (success)
            {
                TopBar.Reset();
                await TopBar.LoadAsync();
                Nav.NavigateTo(AppRoutes.Home, replace: true);
                return;
            }
        }
        else
        {
            AuthState.ClearError();
            _showDevLogin = true;
            _loading = false;
            StateHasChanged();
            return;
        }

        _loading = false;
        StateHasChanged();
    }

    private async Task Retry()
    {
        _loading = true;
        StateHasChanged();

        var success = await AuthState.LoginWithTelegramAsync();
        if (success)
        {
            TopBar.Reset();
            await TopBar.LoadAsync();
            Nav.NavigateTo(AppRoutes.Home, replace: true);
            return;
        }

        _loading = false;
        StateHasChanged();
    }

    private async Task DevLoginManual()
    {
        var input = _handleInput.Trim();
        if (string.IsNullOrWhiteSpace(input))
            return;

        _loading = true;
        StateHasChanged();

        long telegramUserId;
        if (long.TryParse(input, out var parsed) && parsed > 0)
        {
            telegramUserId = parsed;
        }
        else
        {
            var resolved = await AuthState.ResolveTelegramHandleAsync(input);
            if (resolved is null or <= 0)
            {
                _loading = false;
                StateHasChanged();
                return;
            }
            telegramUserId = resolved.Value;
        }

        var success = await AuthState.LoginDevAsync(telegramUserId);
        if (success)
        {
            TopBar.Reset();
            await TopBar.LoadAsync();
            Nav.NavigateTo(AppRoutes.Home, replace: true);
            return;
        }

        _loading = false;
        StateHasChanged();
    }

    private string ResolveRedirect()
    {
        if (string.IsNullOrWhiteSpace(RedirectPath))
            return AppRoutes.Home;

        return RedirectPath.StartsWith('/') ? RedirectPath : AppRoutes.Home;
    }
}
