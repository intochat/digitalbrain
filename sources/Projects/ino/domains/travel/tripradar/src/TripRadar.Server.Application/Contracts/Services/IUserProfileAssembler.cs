using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.Contracts.Services;

public interface IUserProfileAssembler
{
    GetUserProfileResponseDTO Assemble(User user);
}
