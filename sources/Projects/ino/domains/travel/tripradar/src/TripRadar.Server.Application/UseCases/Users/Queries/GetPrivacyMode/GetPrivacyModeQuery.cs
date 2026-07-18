using MediatR;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Queries.GetPrivacyMode;

public sealed record GetPrivacyModeQuery(string Username) : IRequest<Result<bool>>, IAuthorizedRequest;
