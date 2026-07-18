using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.DTO.Models;

public sealed record TokenConsumptionTicket(
    string Username,
    ServiceType ServiceType,
    TokenConsumptionType Type,
    decimal? TokenCost = null);
