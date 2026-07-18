using System.Globalization;
using Microsoft.JSInterop;

namespace TripRadar.MiniApp.Layout;

public partial class MainLayout
{
    private bool _ready;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        try
        {
            await Auth.InitializeAsync();

            if (!Auth.IsAuthenticated)
            {
                await Auth.LoginWithTelegramAsync();
            }

            await ApplyTheme();
            await SyncLanguageFromProfile();
            await TopBar.LoadAsync();
        }
        catch
        {
            // ignored
        }

        _ready = true;

        if (!Auth.IsAuthenticated && !Nav.Uri.Contains(AppRoutes.Auth, StringComparison.OrdinalIgnoreCase))
        {
            Nav.NavigateTo(AppRoutes.Auth, replace: true);
        }

        StateHasChanged();
    }

    private async Task ApplyTheme()
    {
        var scheme = await JS.InvokeAsync<string>("getTelegramColorScheme");
        await JS.InvokeVoidAsync("tripRadar.setDarkMode", scheme == "dark");
    }

    private async Task SyncLanguageFromProfile()
    {
        try
        {
            var alreadySynced = await JS.InvokeAsync<string?>("sessionStore.get", "culture_synced");
            if (alreadySynced == "1") return;

            var profile = await UserApi.GetProfileAsync();
            if (profile?.LanguageCode is not null)
            {
                var currentLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                if (!string.Equals(currentLang, profile.LanguageCode, StringComparison.OrdinalIgnoreCase))
                {
                    await JS.InvokeVoidAsync("sessionStore.set", "app_culture", profile.LanguageCode);
                    await JS.InvokeVoidAsync("sessionStore.set", "culture_synced", "1");
                    Nav.NavigateTo(Nav.Uri, forceLoad: true);
                    return;
                }
            }

            await JS.InvokeVoidAsync("sessionStore.set", "culture_synced", "1");
        }
        catch
        {
            // ignored
        }
    }
}
