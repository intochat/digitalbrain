namespace TripRadar.Server.Infrastructure.Settings;

public class JobSettings
{
    public ResetTokensJobSettings ResetTokensJob { get; set; } = new();

    public MetterBillingJobSettings MetterBillingJob { get; set; } = new();
}
