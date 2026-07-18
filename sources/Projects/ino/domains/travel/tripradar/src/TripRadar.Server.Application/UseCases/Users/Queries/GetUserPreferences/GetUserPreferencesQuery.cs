using MediatR;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Queries.GetUserPreferences;

public sealed record GetUserPreferencesQuery(string Username) : IRequest<Result<UserPreferencesResponseDTO>>, IAuthorizedRequest;
