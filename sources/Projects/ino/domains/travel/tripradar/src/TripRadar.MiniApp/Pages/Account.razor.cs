using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TripRadar.MiniApp.Client.Infrastructure.Models.Common;

namespace TripRadar.MiniApp.Pages;

public partial class Account
{
    private UserProfile? _profile;
    private string _selectedLanguage = "en";
    private bool _openingWebsite;
    private string? _openOnWebsiteError;

    protected override async Task OnInitializedAsync()
    {
        _selectedLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        try
        {
            _profile = await UserApi.GetProfileAsync();
            if (_profile?.LanguageCode is not null)
                _selectedLanguage = _profile.LanguageCode;
        }
        catch
        {
            // ignored
        }
    }

    private async Task OnLanguageChanged(ChangeEventArgs e)
    {
        var lang = e.Value?.ToString() ?? "en";
        var culture = new CultureInfo(lang);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        await JS.InvokeVoidAsync("sessionStore.set", "app_culture", lang);
        try { await UserApi.UpdateProfileAsync(new UpdateProfileRequest(LanguageCode: lang)); }
        catch
        {
            // ignored
        }

        Nav.NavigateTo(Nav.Uri, forceLoad: true);
    }

    private async Task SignOut()
    {
        TopBar.Reset();
        await Auth.ClearAsync();
        Nav.NavigateTo(AppRoutes.Auth, replace: true);
    }

    private async Task OpenOnWebsite()
    {
        if (_openingWebsite)
            return;

        _openingWebsite = true;
        _openOnWebsiteError = null;

        try
        {
            var websiteUrl = await MiniAppConfig.GetWebsiteUrlAsync();
            if (string.IsNullOrWhiteSpace(websiteUrl))
            {
                _openOnWebsiteError = L["AccountOpenOnWebsiteError"].Value;
                return;
            }

            var session = await UserApi.CreatePortableSessionAsync();
            if (session is null || string.IsNullOrEmpty(session.Token) || string.IsNullOrEmpty(session.RefreshToken))
            {
                _openOnWebsiteError = L["AccountOpenOnWebsiteError"].Value;
                return;
            }

            var handoffUrl = $"{websiteUrl.TrimEnd('/')}/auth/session-handoff#at={Uri.EscapeDataString(session.Token)}&rt={Uri.EscapeDataString(session.RefreshToken)}";
            await JS.InvokeVoidAsync("tg.open", handoffUrl);
        }
        catch (Exception)
        {
            _openOnWebsiteError = L["AccountOpenOnWebsiteError"].Value;
        }
        finally
        {
            _openingWebsite = false;
        }
    }
}
