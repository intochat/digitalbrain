using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.DTO.Models;

public record TokenConsumedEvent(string Username, ServiceType ServiceType, TokenConsumptionType Type, decimal? TokenCost);
