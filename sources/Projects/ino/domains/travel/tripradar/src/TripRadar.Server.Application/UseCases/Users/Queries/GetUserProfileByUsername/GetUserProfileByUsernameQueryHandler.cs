using MediatR;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Queries.GetUserProfileByUsername;

public sealed class GetUserProfileByUsernameQueryHandler(
    IUserProfileAssembler userProfileAssembler,
    ICurrentUserContext currentUserContext)
    : IRequestHandler<GetUserProfileByUsernameQuery, Result<GetUserProfileResponseDTO>>
{
    public Task<Result<GetUserProfileResponseDTO>> Handle(GetUserProfileByUsernameQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success(userProfileAssembler.Assemble(currentUserContext.GetRequiredUser())));
    }
}
