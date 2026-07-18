using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Contracts.Requests;

public interface ITripVaultQueryRequest
{
    string? TripVaultName { get; }

    ServiceType ServiceType { get; }

    object GetTripVaultPayload();
}
