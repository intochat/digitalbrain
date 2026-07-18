namespace TripRadar.Server.Application.Settings;

public class PricesCacheSettings
{
    public int ExpirationHours { get; set; }
    public string CacheKey { get; set; } = null!;
}
