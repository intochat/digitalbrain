using MediatR;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Payments.Queries.GetUserSubscription;

public record GetUserSubscriptionQuery(string Username) : IRequest<Result<UserSubscriptionDTO>>, IAuthorizedRequest;
