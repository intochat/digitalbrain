using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Contracts.Requests;

public interface ITokenConsumingRequest : IAuthorizedRequest
{
    ServiceType ServiceType { get; }
}
