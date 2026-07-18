namespace TripRadar.Server.Application.DTO.Responses;

public record GetUserTierUsageResponseDTO(
    string TierName,
    decimal CurrentUsage,
    decimal DailyLimit,
    decimal RemainingRequests,
    double UsagePercentage);

