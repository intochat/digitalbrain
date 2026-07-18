using TripRadar.MiniApp.Client.Infrastructure.Contracts;

namespace TripRadar.MiniApp.Client.Infrastructure.Services.State;

public sealed class UserPreferencesState(IUserManager userApi)
{
    private const string DefaultCurrency = "USD";
    private const string FlightCurrencyKey = "Flight.Currency";

    public string Currency { get; private set; } = DefaultCurrency;

    public async Task LoadAsync()
    {
        try
        {
            var preferences = await userApi.GetPreferencesAsync();
            var value = preferences.FirstOrDefault(p => string.Equals(p.PreferenceTypeDisplayName, FlightCurrencyKey, StringComparison.OrdinalIgnoreCase))?.Value;
            Currency = string.IsNullOrWhiteSpace(value) ? DefaultCurrency : value.Trim().ToUpperInvariant();
        }
        catch
        {
            // non-critical: fall back to default
        }
    }
}