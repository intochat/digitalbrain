namespace TripRadar.Server.Application.DTO.Models;

public record GetOverageUsageDTO(
    string Username,
    string TierName,
    decimal RegularTokensUsed,
    decimal OverageTokensUsed,
    decimal TotalOverageCharges,
    string Currency,
    int Year,
    int Month,
    bool IsEligibleForOverage,
    bool PayAsYouGoEnabled);
