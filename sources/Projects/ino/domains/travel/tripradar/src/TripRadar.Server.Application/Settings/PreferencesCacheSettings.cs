namespace TripRadar.Server.Application.Settings;

public class PreferencesCacheSettings
{
    public string PreferencesCacheKey { get; set; } = string.Empty;
    public string PreferenceCategoriesCacheKey { get; set; } = string.Empty;
    public string AllPreferenceTypesCacheKey { get; set; } = string.Empty;
    public string ServicePreferenceTypesCacheKey { get; set; } = string.Empty;
    public int DefaultTtlMinutes { get; set; }
}
