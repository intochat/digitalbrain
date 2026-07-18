using MediatR;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Payments.Queries.GetPaymentMethods;

public record GetPaymentMethodsQuery(string Username) : IRequest<Result<PaymentMethodsDTO>>, IAuthorizedRequest;
