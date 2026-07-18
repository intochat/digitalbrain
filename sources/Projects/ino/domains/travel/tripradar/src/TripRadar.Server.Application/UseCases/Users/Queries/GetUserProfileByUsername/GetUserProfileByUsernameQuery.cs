using MediatR;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Queries.GetUserProfileByUsername;

public sealed record GetUserProfileByUsernameQuery(string Username) : IRequest<Result<GetUserProfileResponseDTO>>, IAuthorizedRequest;
