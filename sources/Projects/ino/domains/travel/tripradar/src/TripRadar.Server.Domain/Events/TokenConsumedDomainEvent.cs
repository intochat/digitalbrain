using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Domain.Events;

/// <summary>
/// Domain event raised when tokens are consumed by a user.
/// </summary>
/// <param name="Username">The username of the user who consumed tokens.</param>
/// <param name="ServiceType">The type of service for which tokens were consumed.</param>
/// <param name="Type">The type of token consumption (Tier or Overage).</param>
/// <param name="TokenCost">The cost of tokens consumed, if applicable for overage billing.</param>
public record TokenConsumedDomainEvent(string Username, ServiceType ServiceType, TokenConsumptionType Type, decimal? TokenCost) : IDomainEvent;
