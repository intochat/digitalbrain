namespace TripRadar.Server.Infrastructure.Settings;

public class PriceSettings
{
    public string BasicTierPriceId { get; set; } = string.Empty;

    public string EssentialTierPriceId { get; set; } = string.Empty;

    public string AdvancedTierPriceId { get; set; } = string.Empty;

    public string BasicTierYearlyPriceId { get; set; } = string.Empty;

    public string EssentialTierYearlyPriceId { get; set; } = string.Empty;

    public string AdvancedTierYearlyPriceId { get; set; } = string.Empty;
}
