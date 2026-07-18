namespace TripRadar.Server.Infrastructure.Settings;

public class ResetTokensJobSettings
{
    public int BatchSize { get; set; } = 100;
    public int LockTimeoutMinutes { get; set; } = 30;
    public int CacheExpirationHours { get; set; } = 1;
}
