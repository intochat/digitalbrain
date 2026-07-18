namespace TripRadar.MiniApp.Client.Infrastructure.Models.Common;

public static class UserTier
{
    public const string Basic = nameof(Basic);
    public const string Essential = nameof(Essential);
    public const string Advanced = nameof(Advanced);

    public static bool IsPaid(string? tierName) => string.Equals(tierName, Essential, StringComparison.OrdinalIgnoreCase) || string.Equals(tierName, Advanced, StringComparison.OrdinalIgnoreCase);
}