namespace DigitalBrain.Marketplace;

// D9 invited beta: IntoChat keeps only the merchant-of-record cost fee and waives any platform
// fee on top. Both rates are configurable; the platform rate is the post-beta lever.
public sealed class MarketplaceEarningsOptions
{
    public decimal MerchantOfRecordFeeRate { get; set; } = 0.05m;

    public decimal PlatformFeeRate { get; set; }

    public TimeSpan HoldingPeriod { get; set; } = TimeSpan.FromDays(120);

    public decimal PayoutThresholdUsd { get; set; } = 25m;

    public decimal TakeRate => MerchantOfRecordFeeRate + PlatformFeeRate;
}
