namespace TripRadar.Server.Application.Settings;

public class CachingSettings
{
    public bool Enabled { get; set; }
    public int DefaultExpirationHours { get; set; }
    public PricesCacheSettings PricesCache { get; set; } = new();
    public PreferencesCacheSettings Preferences { get; set; } = new();
}
